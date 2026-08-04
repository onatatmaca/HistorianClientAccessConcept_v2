---
description: Proficy Historian ClientAccess API patterns — queries, writes, pagination, error handling
---

# Proficy Historian ClientAccess API

## Assembly Reference
```xml
<Reference Include="Proficy.Historian.ClientAccess.API, Version=1.0.0.0,
    Culture=neutral, PublicKeyToken=651cf43ad2e50609, processorArchitecture=MSIL">
  <HintPath>C:\Program Files\Proficy\Proficy Historian\Assemblies\
             Proficy.Historian.ClientAccess.API.dll</HintPath>
</Reference>
```
Must be installed on the target machine. Not distributed with the project.

## ServerConnection Lifecycle
```csharp
ServerConnection sc = new ServerConnection(new ConnectionProperties
{
    ServerHostName = hostname,
    Username = "",
    Password = "",
    ServerCertificateValidationMode = CertificateValidationMode.None
});
sc.Connect();
bool alive = sc.IsConnected();
sc.Disconnect(); // always call in Dispose / form close
```
**v1 bug:** `ServerConnection` was never disposed. In v2, implement `IDisposable` on services
that own connections and call `Disconnect()` in `Dispose()`.

**IP addresses & the DNS-identity check:** the raw API rejects IP connects — WCF checks
the server's claimed name ("expected DNS identity … but the remote endpoint provided
DNS claim 'TESTSV1'") and `CertificateValidationMode.None` does not bypass that.
v2 works around it: `Services/ProficyEndpoint.PrepareForIp` prebuilds the channel
factory with a lenient `IdentityVerifier` (skips only the name comparison; TLS + cert
validation mode stay on). Hostname connects keep the stock check. Scripts using the
API directly must still connect by hostname.

**Port:** no `ConnectionProperties` member — the URI port comes from the public static
`Proficy...Internal.HistorianAddress.TcpPort` (default 13000) or the `TcpPortNumber`
appSetting. v2 sets it per connect via `ProficyEndpoint.SetPortForNextConnect`, so the
server fields accept `host:port`.

**Credentials:** an empty username uses the Windows session (works on the Historian box
itself). Remote servers may reject empty usernames — v2 reads optional
`HistorianUsername` / `HistorianPassword` from app.config
(`HistorianConnectionService.BuildProperties`).

## Tag Queries (Paginated)
```csharp
TagQueryParams query = new TagQueryParams { PageSize = 100 };
query.Criteria.TagnameMask = "SomePrefix*";
query.Criteria.DataType = Tag.NativeDataType.Float;
List<Tag> allTags = new List<Tag>();
List<Tag> tmp;
while (sc.ITags.Query(ref query, out tmp))
    allTags.AddRange(tmp);
allTags.AddRange(tmp); // last page is returned after while exits — must add
```
**v1 bug:** The double `AddRange` pattern is correct for the API but was easy to miss.
The `while` loop exits when the last page is returned (returns `false`), but `tmp` still
contains the final page — it must be added after the loop.

## Tag Existence Check
```csharp
TagQueryParams q = new TagQueryParams { PageSize = 1 };
q.Criteria.TagnameMask = "ExactTagName";
List<Tag> result;
sc.ITags.Query(ref q, out result);
bool exists = result != null && result.Count > 0;
```

## Timestamps — the API frame is UTC, and `DateTimeKind` changes your query (PROVEN)

**The old claim "Historian returns local by default" was WRONG.** Measured live on TESTSV1
(2026-07-16, `KindProbe.exe` / `DupCheck.exe`):

- **Every returned timestamp has `Kind=Utc`.** A live-collecting tag's newest sample equalled
  `DateTime.UtcNow`, **not** `DateTime.Now` (server in Germany, CEST +2).
- **The `Kind` of the QUERY START silently shifts the query by the UTC offset:**

  | start `Kind` | Feb (CET +1) | May (CEST +2) |
  |---|---|---|
  | `Unspecified` (`ParseExact`) | firstTs **−59.2 min** | **−110.8 min** |
  | `Local` (a DateTimePicker) | **−59.2 min** | **−110.8 min** |
  | `Utc` (API-derived) | **+0.7 min** ✅ | **+1.3 min** ✅ |

  Local/Unspecified ⇒ the API converts local→UTC (correct per-date DST offset) ⇒ **the query starts
  early by the offset and returns samples BEFORE the requested `from`**. `Kind=Utc` ⇒ sent as-is.

### Rules that follow (non-negotiable)
1. **Never let a read result reach a delete.** `IData.Delete(string[], DateTime[], out ItemErrors)`
   is the ONLY overload — it has no range form, so it deletes exactly the ticks you pass. An
   over-read + delete destroys data outside your window. This already happened once (see
   `known-issues.md`). Delete only from the journal.
2. **Always clip a read on BOTH bounds**: `if (ts > to) break;` is not enough — add
   `if (ts < from) continue;` (a `continue`, never a `break`; the stream can start before `from`).
   `SampleFilter.ParseAndClip` does this for the app.
3. **Advance a pagination cursor with `lastTs.AddTicks(1)` — it preserves `Kind=Utc`.**
   `new DateTime(lastTs.Ticks + 1)` **DROPS the Kind** → every chunk re-reads ~1–2 h → duplicate,
   non-monotonic output. (The app is correct; several throwaway tools were not.)
4. **Never compare/mix `DateTime.Now` or picker values with API times** — .NET compares raw Ticks
   and ignores `Kind`, so the result is silently off by 1–2 h. In this app that is already handled:
   `HistorianDataService` converts at the boundary and everything above it is LOCAL, so plain
   `DateTime.Now` is correct. Do not add conversions elsewhere.
5. **A read must never look like "no data" when it failed.** Reads check `ItemErrors` and throw
   (`ThrowOnItemErrors`) so `RetryHelper` retries. An errored, silently-empty TARGET read would make
   `SyncPlanner` fall back to exact-diff, mass-copy, journal it — and a later revert would delete
   pre-existing samples. Verified live: an empty window **and** a nonexistent tag both return
   `ItemErrors=0`, so this never fires on the normal "no data here" case.

## Data Queries

### Interpolated (last N samples over a window)
```csharp
DataQueryParams query = new InterpolatedQuery(startTime, endTime, sampleCount, tagName)
{
    Fields = DataFields.Time | DataFields.Value | DataFields.Quality
};
ItemErrors errors;
Proficy.Historian.ClientAccess.API.DataSet set = new DataSet();
sc.IData.Query(ref query, out set, out errors);
```

### Raw by Time (all raw samples from a start time, paginated)
```csharp
DataQueryParams query = new RawByTimeQuery(startTime, tagName)
{
    Fields = DataFields.Time | DataFields.Value
};
DataSet all = new DataSet();
DataSet tmp = new DataSet();
ItemErrors errors;
while (sc.IData.Query(ref query, out tmp, out errors))
    all.AddRange(tmp);
all.AddRange(tmp); // same last-page pattern as tag queries
```

## Reading Sample Values
```csharp
for (int i = 0; i < set.TotalSamples; i++)
{
    DateTime ts  = set[tagName].GetTime(i);
    object   val = set[tagName].GetValue(i);   // may be null
    double   pct = set[tagName].GetQuality(i).PercentGood();
}
```

## Writing Float Samples
```csharp
DataSet writeSet = new DataSet();
writeSet[tagName] = new DataSamples<float>
{
    Times          = new DateTime[] { timestamp },
    Values         = new float[]    { value },
    ImplicitQuality = DataQuality.Good
};
ItemErrors writeErrors;
sc.IData.Add(writeSet, false, out writeErrors);
// Second arg is errorOnReplace (verified by reflection on the 1.6.1.0 DLL:
// Add(DataSet dataset, Boolean errorOnReplace, ItemErrors& errors)).
// false = silently overwrite an existing sample at the same timestamp — right for
// backfill. Out-of-order historical writes need NO flag (older docs here wrongly
// called this "allowOutOfOrder").
```

## ItemErrors Handling
```csharp
if (writeErrors != null && writeErrors.Count > 0)
{
    foreach (var kv in writeErrors)
        Log($"Write error on tag {kv.Key}: {kv.Value}");
}
```

## Server / Collector Info
```csharp
HistorianConfiguration cfg = sc.IServer.GetConfiguration();
// cfg.ActualTags = total tag count

List<DataStore> collectors;
sc.IDataStores.Query("*", out collectors);

CollectorStatistics stats = sc.ICollectors.GetStatistics(hostName);
```

## Multi-Field Tags (proof of concept — not core workflow)
See `On_cmdAddMultiFieldTag_Click` in v1 `Main.cs` for full example.
Uses `UserDefinedType`, `MultiField`, `Field`, `MultiFieldValue`, `FieldValue`, `DetailedValue`.
