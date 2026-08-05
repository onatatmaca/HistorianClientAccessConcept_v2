# Known Issues — v2 Archive (Phases 5–6, resolved & stable)

Resolved v2 issues old enough that they no longer change day-to-day work, moved here
from `known-issues.md` to keep that file under 200 lines. Newer items (Phase 8+) stay
in [`known-issues.md`](known-issues.md); v1 pitfalls live in
[`known-issues-v1.md`](known-issues-v1.md).

---

## BUG: Per-Tag Gap Analysis Produces False Gaps on Bimodal/Deadband Data
**Location:** `GapAnalysisService.Analyze`

Variable-interval tags (bimodal or deadband) made the median-based detector latch onto
the fast interval and flag every quiet stretch as a gap (hundreds of 0m16s "gaps", 17%
coverage that should be ~95%). Phase 5 restricted analysis to HistSync; Phase 6 re-enabled
per-tag with `threshold = max(median × 1.5, MinimumGapSeconds)`; the floor then hid
isolated missing samples, so the **final fix** decoupled backfill from gap windows
entirely: backfill diffs actual timestamps between servers; gap analysis is display-only.

## One-liners (fixed Phase 5/6)
- **DateTimePicker showed arrows, not a calendar** — `MakeDtp()` had `ShowUpDown = true`.
- **Gap grid Duration column cut off** — `FillWeight` without `AutoSizeColumnsMode = Fill`.
- **No UI refresh after backfill** — `AutoRefreshAfterBackfill()` re-runs the analysis, re-reads
  the data grids and exits compare mode.
- **Verification failure counted as success** — `BatchesSucceeded++` ran before `VerifyWrite`;
  now gated on `writeOk && verifyOk`, and a failed verify increments `BatchesFailed`.
- **CancellationTokenSource leaked per operation** — every handler allocated a new CTS without
  disposing the old; `ResetCts()` disposes then re-allocates, `OnFormClosing` disposes the last.

## BUG: Empty-server side blocked backfill (fixed Phase 6)
`Analyze` returned early with no gaps for a 0-sample server, so nothing was offered
for backfill even when the other side had data. Now emits one whole-period `GapWindow`
(split into batches) so feasibility marking works; the preview flow offers only
directions with batches.

## BUG: Verify false-negative from sub-second rounding (fixed Phase 6)
Verify window `[firstTime, lastTime + 1 tick)` missed samples because Historian stores
second-precision timestamps (12:54:30.123 is stored as 12:54:30, before the query
start). Widened to `[first − 1s, last + 1s]`. (Phase 8 later made verify per-sample
at whole-second resolution — see `known-issues.md`.)

## BUG: Backfill journal never saved (silent serialization crash) (fixed Phase 8)
**Location:** `Models/BackfillJournal.cs` · `BackfillJournalEntry.RevertedLocal`

Every backfill silently failed to journal → Backfill History always empty → nothing revertable.
Root cause: `RevertedLocal` was a non-nullable `DateTime` defaulting to `DateTime.MinValue`
(0001-01-01). `DataContractJsonSerializer` converts DateTime to UTC, and 0001-01-01 *local* → UTC
underflows `DateTime.MinValue` in any timezone **ahead of UTC** (the dev/test site is UTC+1/+2),
throwing `SerializationException`; `BackfillJournalService.Save` swallowed it in a bare `catch {}`.
**Fix:** `RevertedLocal` is now `DateTime?` (null until reverted). (Lesson: don't serialize a
default `DateTime` via DataContractJsonSerializer; use nullable or UTC ticks — the same lesson
later immunized the journal's `long[] Ticks` against the 2026-07 UTC-frame bug class.)

## BUG: Backfill re-copies the same samples forever; false "succeeded" (fixed Phase 8)
**Location:** `Forms/MainForm.cs` · `ExecuteBackfill` diff + verify; `TagSelectionDialog`

A backfill reported "succeeded" but coverage never changed and the same samples could be re-copied
indefinitely. Root cause: the direct-comparison diff compared **exact ticks**, but Historian stores
at **second** precision (12:54:30.123 → 12:54:30), so the next diff saw the original tick as still
missing. The old ±1s **count-based** verify (`actual >= expected`) passed whenever *any* nearby
sample existed, masking it. **Fix:** diff, verify and journaled timestamps all compare at
whole-second resolution (`SampleFilter.ToSecondTicks`); verify confirms each written second is
actually present, so a write that doesn't land is reported failed instead of looping.

## NOTE: Proficy DataSet is not IDisposable (closed Phase 6)
Per GE docs, `DataSet` inherits `Dictionary<string,IDataSamples>` and adds no
interfaces — no `using` needed, no leak.

## Phase 9 items (moved from known-issues.md, Phase 12a)

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


## Phase 10 items (moved from known-issues.md, Phase 12d)

## BUG: Coverage collapsed on deadband tags — median-based gap rule (fixed Phase 10)
`GapAnalysisService.Analyze`. TEMP_02_WS logs every ~6 min (median) but normally stays quiet
30–60 min (deadband), so `max(median×1.5, 120s)` ≈ 9.5 min flagged every normal quiet period →
**41 % coverage on a healthy tag**. Fix: `max(p90(intervals) × GapThresholdMultiplier,
MinimumGapSeconds)` — if ≥10 % of a tag's intervals reach a duration, that duration is its
cadence. The percentile index is capped below the max so one real outage in a sparse window
cannot masquerade as cadence. Verified live: 41 % → 100 %/100 % on the same window.

## BUG: Exact-second diff = phantom copies on redundant collectors (fixed Phase 10)
The two plant servers collect INDEPENDENTLY — the same values logged 5–120 s apart — so an
exact-second diff called them all "missing": measured live, GASDRUCK_01_GAA showed **33,376 of
47,474** samples missing when ~98 genuinely were. A backfill would have permanently interleaved
both collectors' streams, in both directions, on every tag. Fix: `SyncPlanner.Plan` auto-detects
per tag — exact-second match ≥90 % ⇒ aligned streams → exact diff (catches isolated misses);
otherwise → copy only source samples inside real TARGET OUTAGES. Every surface uses the planner,
so they all report what a restore would actually write.

## BUG: ReadRawInRange paged to the archive end + cursor-leak trap (fixed Phase 10)
It ran `RawByTimeQuery` to the END of the archive and clipped client-side (a 13-day window on
the 2-year Genthin archive read ~50× too much). The naive fix — breaking out of the pagination
early — **leaks a server-side cursor** per abandoned query until expiry ("Maximum number of
cached items exceeded", which then fails later queries too). Fix: bounded `RawByNumberQuery`
chunks (5000/call, each drained), stopping at the range end. **Never abandon a paged RawByTime
query** — including in throwaway scripts.
# Bugs Fixed vs v1 (historical tracking table, moved from roadmap.md)

| Bug | Phase Fixed | Notes |
|-----|-------------|-------|
| ServerConnection never disposed | 1 | Fixed in `HistorianConnectionService.Dispose()` |
| Log uses dtTimestamp instead of DateTime.Now | 1 | Fixed in new `Log()` helper |
| HasSampleInRange strict boundary exclusion | 2 | Use `>=` start, `<` end — binary search in MarkBackfillFeasibility |
| TagQueryParams not reset between servers in stats | 1 | New params instance per server |
| All ops on UI thread | 1 | async/await + Task.Run in service calls |
| No retry on API calls | 2 | Retry helper in HistorianDataService (configurable via app.config) |
| HistSync tag name hardcoded | 2 | Moved to app.config `SyncTagName` |
| Batch size hardcoded (10 min) | 2 | Moved to app.config `BatchSizeMinutes` |
| Quality forced to Good on backfill writes | 2 | `WriteFloatSamplesWithQuality` preserves source quality |
| Bare catch blocks in disconnect | audit | Added Trace.TraceWarning logging |
| SetBusy re-entrancy (buttons not disabled) | audit | Action buttons disabled during ops |
| ReadRawInRange missing quality | audit | Now returns `(Time, Value, Quality)` tuple |
| Unsorted input to GapAnalysisService | audit | Auto-sorts if needed |
| Coverage wildly wrong on bimodal tags | 5 | HistSync-only gap analysis eliminated false gaps |
| Radio buttons confused gap analysis scope | 5 | Removed — HistSync-as-master (per-tag returned in Phase 6) |
| DateTimePicker showed arrows not calendar | 5 | `ShowUpDown = false` |
| Gap grid Duration column cut off | 5 | `AutoSizeColumnsMode = Fill` |
| No UI refresh after backfill | 5 | `AutoRefreshAfterBackfill()` re-runs gap analysis |
| Single-tag backfill too limiting | 5 | Multi-tag via `TagSelectionDialog` |
| Data grids stayed stale after backfill | 6 | `AutoRefreshAfterBackfill` also re-reads primary/secondary grids |
| Verify-write failure counted as success | 6 | `BatchesSucceeded++` gated on writeOk AND verifyOk |
| CancellationTokenSource leaked per op | 6 | `ResetCts()` disposes before re-allocating |
| Gap grid columns cramped in right panel | 6 | Reduced columns; hover tooltip carries full detail |
| No pre-flight stats before backfill | 6 | `TagSelectionDialog` per-tag source/target/diff stats |
| No detailed post-backfill summary | 6 | `SyncReportDialog` with per-tag grid + CSV/TXT export |
| Empty-server gap detection blocked backfill | 6 | Whole-period gap emitted when sample count is 0 |
| Deadband tags produced hundreds of false gaps | 6 | `MinimumGapSeconds` floor (default 120s) |
| Isolated missing samples below gap floor never copied | 6 | Backfill switched to direct timestamp comparison |
| Verify false-negative from sub-second rounding | 6 | Verify window widened to ±1s |
| Backfill journal never saved (silent serialization crash) | 8 | `RevertedLocal` made nullable |
| Backfill re-copies samples forever; false "succeeded" | 8 | Diff + verify at whole-second resolution |
| Backfill "forever" on live servers (in-flight samples) | 9 | Live-edge clamp: eval end capped at now − `LiveEdgeGraceSeconds` |
| Gap analysis read to end of archive | 9 | `SafeReadTimes` now uses `ReadRawInRange` |
| Two stop/cancel buttons + hidden progress bar | 9 | Single modal `ProgressDialog` with one Cancel |
| Gaps hard to find/compare across servers | 9 | Full-width dual-track `GapTimeline` + copy strip + click-zoom |
