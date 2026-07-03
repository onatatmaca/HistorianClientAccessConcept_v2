---
description: Bugs fixed in v2 (phase 8+) and open items. Older resolved items in known-issues-archive.md, v1 history in known-issues-v1.md.
---

# Known Issues — v2

Current v2 bugs/design defects and their resolutions. Phases 5–6 (resolved & stable)
moved to [`known-issues-archive.md`](known-issues-archive.md); v1 pitfalls are in
[`known-issues-v1.md`](known-issues-v1.md).

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

## BUG: Backfill journal never saved (silent serialization crash) — Phase 8
**Location:** `Models/BackfillJournal.cs` · `BackfillJournalEntry.RevertedLocal`

Every backfill silently failed to journal → Backfill History always empty → nothing
revertable. Root cause: `RevertedLocal` was a non-nullable `DateTime` defaulting to
`DateTime.MinValue` (0001-01-01). `DataContractJsonSerializer` converts DateTime to UTC,
and 0001-01-01 *local* → UTC underflows `DateTime.MinValue` in any timezone **ahead of UTC**
(the dev/test site is UTC+1/+2), throwing `SerializationException`. `BackfillJournalService.Save`
swallowed it in a bare `catch {}`, so it failed invisibly.

**Fix:** `RevertedLocal` is now `DateTime?` (null until reverted) so the bad value is never
serialized. Confirmed by a standalone save→load round-trip. (Lesson: don't serialize a
default `DateTime` via DataContractJsonSerializer; use nullable or UTC ticks.)

---

## BUG: Backfill re-copies the same samples forever; false "succeeded" — Phase 8
**Location:** `Forms/MainForm.cs` · `ExecuteBackfill` diff + verify; `TagSelectionDialog`

A backfill reported "succeeded" but coverage never changed and the same samples could be
re-copied indefinitely. Root cause: the direct-comparison diff compared **exact ticks**.
Historian stores at **second** precision, so a sub-second source sample (12:54:30.123) is
stored as 12:54:30; the next diff sees the original tick as still missing and copies it again.
The old ±1s **count-based** verify (`actual >= expected`) passed whenever *any* nearby sample
existed, masking it as success.

**Fix:** the diff, the verify, and the journaled timestamps all compare at whole-second
resolution (`SampleFilter.ToSecondTicks`). The verify now confirms each written second is
actually present (honest per-sample check), so a write that doesn't land (e.g. archive
compression) is correctly reported as failed instead of looping.

---

## NOTE: WinForms UIA quirks (dev tooling only)
Driving the UI via UI Automation from scripts proved flaky for element enumeration on
this form (buttons intermittently invisible to `FindAll`). The screenshot/verification
scripts therefore click by coordinates. Not a product issue — end users don't use UIA.
