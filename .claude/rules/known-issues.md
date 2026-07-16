---
description: Bugs fixed in v2 (phase 8+) and open items. Older resolved items in known-issues-archive.md, v1 history in known-issues-v1.md.
---

# Known Issues — v2

Current v2 bugs/design defects and their resolutions. Phases 5–6 (resolved & stable)
moved to [`known-issues-archive.md`](known-issues-archive.md); v1 pitfalls are in
[`known-issues-v1.md`](known-issues-v1.md).

---

## INCIDENT + OPEN DEFECTS: UTC frame vs `DateTime.Now` (2026-07-16, audited)

**Proven** (live probes, see [`historian-api.md`](historian-api.md)): the API frame is **UTC**
(returned `Kind=Utc`), and a `Local`/`Unspecified` **query start** is converted local→UTC, so the
query **starts early by the UTC offset** (1 h winter / 2 h summer) and returns samples before `from`.

### Data loss (external tooling, NOT the app) — fixed + data restored
A throwaway helper read `[from,to]` with only `if (ts > to) break;` (**no `ts < from` guard**) and
deleted every timestamp it read ⇒ **~1 h of Secondary data BEFORE the window was destroyed** (API
hour `2026-02-09 23` = Historian-local `2026-02-10 00:00–01:00`); backfills, correctly scoped by
`SyncPlanner`'s `t >= evalFrom`, never restored it. Restored from Primary (2,343 samples).
**The app cannot do this**: its only `IData.Delete` consumes **journal ticks**, never a read
(`MainForm.cs:1674`), and `SampleFilter.cs:35` has the `if (s.Time < start) continue;` guard.
`IData.Delete` has no range overload (verified by reflection).

### FIXED — the app now converts at the API boundary (single point)
`HistorianDataService` gained `ToApi()` / `FromApi()`; **the API frame stops at that service and the
rest of the app works in LOCAL time**. Applied to every crossing: query bounds, returned times,
`WriteFloatSamplesWithQuality`, `DeleteSamples`, `ReadRaw`, `ReadInterpolated`. `ReadRawInRange`
runs its chunk loop in UTC (`cursor = lastTs.AddTicks(1)` **preserves Kind** — never
`new DateTime(ticks+1)`), clips in UTC, then hands back local.

**Verified live** (2026-07-16, picker-style `Kind=Local` window `2026-02-10 00:00→00:03`): the app
now returns `00:00:50=50.8, 00:01:00=51.1, 00:01:10=51.4 …` — **identical to Historian Trend**,
`Kind=Local`. Previously it returned `00:00:40=51.3` (the UTC frame).

This one change fixed **by construction**: the dead live-edge guard, the scheduler's future-window
no-op, the empty "Last 1h" preset, the ~1–2 h window shift (first hour silently dropped: 8,383 read
→ 8,167 kept), and `GapAnalysisService`'s phantom trailing gray band — all were `DateTime.Now`
compared against UTC sample times; both sides are now local.

Also fixed: reads now `ThrowOnItemErrors` (an errored read must never look like "no data" — that
would force exact-diff on an empty target, mass-copy, journal it, and a later revert would delete
pre-existing data). Verified live that an empty window **and** a nonexistent tag both return
`ItemErrors=0`, so it cannot fire on the normal empty case. `LiveEdgeGraceSeconds` now requires
`> 0` (a 0 grace disabled the write/collector race guard).

**Journal frame — deliberately unchanged (this was the landmine):** journals on disk hold **UTC**
ticks. Journaling now converts `bs.Time.ToUniversalTime()` and revert re-tags
`new DateTime(tk, DateTimeKind.Utc)`, so **legacy and new entries revert identically with no
migration**. Do NOT "simplify" this to local ticks — old journals would then delete at an instant
1–2 h off, i.e. real plant data.

**Never affected:** copied values always landed at the correct absolute instant (same frame both
sides), and revert always deleted the correct instant (journal stores `long[] Ticks`, never
`DateTime`, so the serializer never converted them). No data was ever corrupted by this class.

**Still open:** revert reports *requested* not *confirmed* deletions (`MainForm.cs:1693`, no
read-back) — fails safe (deletes nothing, but the message would be optimistic).

---

## BUG: Coverage collapsed on deadband tags — median-based gap rule (fixed Phase 10)
**Location:** `GapAnalysisService.Analyze`

Real plant tag TEMP_02_WS logs every ~6 min (median) but normally stays quiet 30–60 min
(deadband). `threshold = max(median×1.5, 120s)` ≈ 9.5 min flagged every normal quiet
period → 41% coverage shown for a healthy tag. **Fix:** per-tag rule =
`max(p90(intervals) × GapThresholdMultiplier, MinimumGapSeconds)` — if ≥10% of a tag's
intervals reach a duration, that duration is its cadence. The `Percentile` index is
capped below the max so a lone real outage in a sparse window can't masquerade as
cadence. Rule shown on each timeline track ("gap rule: silence > 1h 0m"). Verified
live: TEMP_02_WS now 100%/100% on the exact window that showed 41%.

## BUG: Exact-second diff = phantom copies on redundant collectors (fixed Phase 10)
**Location:** everywhere the whole-second diff ran → new `Services/SyncPlanner`

The two plant servers collect INDEPENDENTLY — same values logged seconds apart
(offsets 5–120s). The exact-second diff reported them all as "missing": measured live,
GASDRUCK_01_GAA showed 33,376 of 47,474 samples "missing" when ~98 genuinely were;
TEMP_02_WS showed 707 where 599 had an identical-value partner within 30s. A backfill
would have permanently interleaved both collectors' streams (double density, both
directions, every tag). **Fix:** `SyncPlanner.Plan` auto-detects per tag:
exact-second match rate ≥90% ⇒ aligned streams (same-source data, e.g. HistSync or
tool-written) → keep exact diff (catches isolated misses); otherwise → copy ONLY
source samples inside real TARGET OUTAGES (silence > the tag's own gap rule).
ExecuteBackfill, both preview dialogs, the amber strip and the missing-data table all
use the same planner — every surface shows what a backfill would actually write.

## BUG: ReadRawInRange paged to the archive end + server cursor-leak trap (fixed Phase 10)
**Location:** `HistorianDataService.ReadRawInRange`

Despite the Phase 9 claim, it still ran `RawByTimeQuery` to the END of the archive and
clipped client-side (a 13-day window on the 2-year Genthin archive read ~50× too much).
The naive fix — breaking out of the pagination early — LEAKS a server-side cursor per
abandoned query until expiry (verified live: "Maximum number of cached items exceeded",
which then fails later queries too). **Fix:** bounded `RawByNumberQuery` chunks
(5000/call, each drained), stopping at the range end. Never abandon a paged RawByTime
query — that applies to any script touching the API as well.

## BUG: "We can backfill forever" on live servers — live-edge diff (fixed Phase 9)
**Location:** `Forms/MainForm.cs` · `ExecuteBackfill`, preview dialogs

With the evaluation end at "now", every diff run found samples the target "lacked" that
were simply still in flight (source collector writes first; the mirror lags seconds).
Each backfill run therefore always reported something new to copy — feeling like an
endless backfill even on perfectly healthy servers.

**Fix:** every write path clamps the evaluation end to `now − LiveEdgeGraceSeconds`
(app.config, default 120s): `ExecuteBackfill` itself, both Copy buttons (so the
TagSelectionDialog counts match what is written), Preview & Backfill, and scheduled
runs. Gap ANALYSIS is intentionally not clamped — the display tells the truth;
only write planning ignores the live edge.

Note the remaining, *correct* reasons coverage never reaches 100 %:
- **Gaps present on BOTH servers** (plant outage / tag logged nothing) can never be
  filled by a sync — the timeline now shows these gray ("missing on both") instead of
  red, so they stop looking like sync failures.
- Samples the target **rejects/compresses** are honestly reported failed each run
  (see the Phase 8 whole-second verify) instead of silently "succeeding".

---

## BUG: Gap analysis read to the end of the archive (fixed Phase 9)
**Location:** `Forms/MainForm.cs` · `SafeReadTimes`

`SafeReadTimes` used `ReadRaw(conn, tag, from)` — a RawByTime query paged until the
END of the archive — then filtered to `[from, to]` client-side. With real plant data
(2+ years of archive) analyzing one week at the start of the archive read months of
samples for nothing. Now uses `ReadRawInRange` (stops paging at the range end).

---

## RESOLVED: IP addresses + custom ports now supported (Phase 9)
**Location:** `Services/HostInputParser`, `Services/ProficyEndpoint`, `HistorianConnectionService`

Raw API behavior: connecting with an IP fails WCF's DNS-identity check (*"expected DNS
identity … but the remote endpoint provided DNS claim 'TESTSV1'"*) —
`CertificateValidationMode.None` does NOT bypass it and `ConnectionProperties` has no
identity override or port property (verified by reflection + decompilation).

**v2 solution** — both server fields accept `host`, `host:port`, `ip`, `ip:port`:
- **Port**: the API builds its net.tcp URI from the public static
  `HistorianAddress.TcpPort` (default 13000, or the `TcpPortNumber` appSetting).
  `ProficyEndpoint.SetPortForNextConnect` sets it immediately before each Connect
  (connections open sequentially, so per-server ports work).
- **IP**: `ProficyEndpoint.PrepareForIp` prebuilds the WCF channel factory exactly as
  `ServerConnection.Connect()` would (replicated from the decompiled 1.6.1.0 assembly)
  but swaps in an `IdentityVerifier` that skips ONLY the DNS-name comparison. TLS and
  the configured certificate validation mode still apply. Hostname connects never take
  this path — they keep the full vendor-stock identity check. If a future ClientAccess
  version changes internals, the helper throws a clear "use the hostname" message.
- Verified live: app connected to 192.168.50.186/.187 by IP, browsed, analyzed and
  previewed real data. (The server does NOT host the `Unsecured` endpoint, so that
  simpler path was not available.)

Optional login for remote servers that reject empty usernames: app.config
`HistorianUsername` / `HistorianPassword` (empty = Windows session, the normal case
when the tool runs on the Historian box itself).

---

## DOC FIX: IData.Add's second argument is errorOnReplace, not "allowOutOfOrder"
Verified by reflecting the v1.6.1.0 DLL: `Add(DataSet dataset, Boolean errorOnReplace,
ItemErrors& errors)`. Passing `false` (as we always did) means "silently replace an
existing sample at the same timestamp" — which is what backfill wants. The old
`historian-api.md` claim that it was an out-of-order flag was wrong; out-of-order
historical writes need no flag at all.

---

## NOTE: WinForms UIA quirks (dev tooling only)
Driving the UI via UI Automation from scripts proved flaky for element enumeration on
this form (buttons intermittently invisible to `FindAll`). The screenshot/verification
scripts therefore click by coordinates. Not a product issue — end users don't use UIA.
