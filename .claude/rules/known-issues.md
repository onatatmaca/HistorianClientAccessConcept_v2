---
description: Bugs fixed in v2 (phases 5–6) and open items. v1 history lives in known-issues-v1.md.
---

# Known Issues — v2

v2 bugs and design defects caught during development and their resolutions.
For v1 pitfalls to avoid, see [`known-issues-v1.md`](known-issues-v1.md).

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

## BUG: Per-Tag Gap Analysis Produces False Gaps on Bimodal/Deadband Data (v2)
**Location:** `GapAnalysisService.Analyze`

Tags with variable-interval sampling — bimodal (pairs 1s apart, then 15s) OR deadband
(1s when changing, 30s+ when stable) — cause the median-based detector to latch onto
the fast interval, then flag every normal quiet stretch as a gap. Observed symptom:
hundreds of 0m16s "gaps" after a successful backfill, 17% coverage that should be ~95%.

**Phase 5 attempt:** Gap analysis restricted to HistSync only (no per-tag option).
**Phase 6 update (first pass):** Per-tag analysis re-enabled. To mitigate false positives,
the threshold became `max(median × 1.5, MinimumGapSeconds)` with `MinimumGapSeconds=120`
by default.
**Phase 6 update (second pass):** The floor solved the noise but created a second problem
— isolated missing samples (20-second gaps on Secondary while Primary has a sample) fell
BELOW the floor, so gap analysis missed them and the backfill never tried to copy them.

**Final fix:** Backfill no longer uses gap windows to decide what to copy. It reads both
servers' samples for the full evaluation range and does a direct timestamp diff (source
timestamps not present on target). Gap analysis is now ONLY for the UI display (coverage
bars + gap grid), completely decoupled from backfill planning. See `sync-workflow.md`.

---

## BUG: DateTimePicker Shows Arrows Instead of Calendar Dropdown (v2, fixed Phase 5)
**Location:** `MakeDtp()` in `MainForm.Designer.cs`

`ShowUpDown = true` caused DateTimePicker to show up/down arrows instead of a calendar popup.

**Fix:** Changed to `ShowUpDown = false`.

---

## BUG: Gap Grid Duration Column Cut Off (v2, fixed Phase 5)
**Location:** `SetupGapGrid()` in `MainForm.Designer.cs`

The gap grid used `FillWeight` on columns but didn't have `AutoSizeColumnsMode = Fill`, so
the Duration column was truncated on the right edge.

**Fix:** Added `gridGaps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill`.

---

## DESIGN: No UI Refresh After Backfill (v2, fixed Phase 5)
After `ExecuteBackfill` completed, the gap analysis results, coverage bars, and data tables
were not refreshed. The user had to manually re-run gap analysis to see updated coverage.

**Phase 5:** Added `AutoRefreshAfterBackfill()` which calls `RunGapAnalysis` after every backfill.
**Phase 6:** Extended to also re-read primary/secondary data grids and exit compare mode.

---

## BUG: Verification failure was counted as success (v2, fixed Phase 6)
**Location:** `Forms/MainForm.cs` · `ExecuteBackfill`

The original flow incremented `tagResult.BatchesSucceeded++` immediately after write errors
were empty — BEFORE the `VerifyWrite` read-after-write check ran. If verification reported
`Actual < Expected`, the batch was still counted as succeeded and the mismatch was only
appended to `tagResult.Errors`. The run report's success count was misleading.

**Fix:** Gate `BatchesSucceeded` / `SamplesWritten` increments on both `writeOk` AND
`verifyOk`. On verification failure, increment `BatchesFailed` instead.

---

## BUG: CancellationTokenSource leaked on repeated operations (v2, fixed Phase 6)
**Location:** `Forms/MainForm.cs` — every async handler

Each long-running op did `_cts = new CancellationTokenSource()` without disposing the
previous instance. Over a session with many operations, these accumulated.

**Fix:** New `ResetCts()` helper disposes then re-allocates. `OnFormClosing` now disposes
the final `_cts` and the auto-analyze `Timer`.

---

## BUG: Empty-server side blocked backfill (v2, fixed Phase 6)
**Location:** `Services/GapAnalysisService.Analyze`

When a server had 0 samples for the evaluation period, `Analyze` returned early with
`HasData=false` and an empty `Gaps` list. That meant:
- `_lastSecondaryResult.HasGaps == false`, so the backfill flow skipped Secondary's
  "direction" entirely and fell through to Primary's gaps
- Primary's gaps couldn't backfill either (Secondary was empty = no source data)
- User saw "No backfillable batches" even when the other side had plenty to copy

**Fix:** When `sampleTimes.Count == 0` AND `evalFrom`/`evalTo` are defined, emit a single
`GapWindow` spanning the entire evaluation period (split into batches). Now
`MarkBackfillFeasibility` correctly identifies batches that overlap with the OTHER server's
data, and the user can backfill the empty server from whichever samples exist on the source.

Also in `btnBackfillPreview_Click`: rewrote the flow to offer only directions that have
`batchCount > 0` instead of hard-coded "try Secondary first else Primary".

---

## BUG: Verify false-negative due to sub-second timestamp rounding (v2, fixed Phase 6)
**Location:** `Forms/MainForm.cs` · `ExecuteBackfill` verify step

Observed in the field: after a successful write, the verify read returned `0 / 1` for
single-sample batches and `59 / 60` for multi-sample batches. The write had succeeded
(errors were empty), but verify was reporting a false failure. Root cause: my verify
window was `[firstTime, lastTime + 1 tick)` (100 ns on the high end). Historian stores
timestamps at **second-level precision** and rounds away sub-second components. For a
sample written at `T = 12:54:30.123`, the stored timestamp is `12:54:30` — which is
BEFORE the query start `12:54:30.123`, so the server returned nothing.

**Fix:** widened verify to `[firstTime - 1s, lastTime + 1s]`. Historian's rounding is
safely within ±1s. Over-counting nearby existing samples is acceptable because we check
`actual >= expected`, not exact equality. Writes themselves were always correct; only the
verification was lying. Users who saw false "100 failed" reports actually had their data
written successfully — re-running backfill would see nothing missing and confirm sync.

---

## NOTE: Proficy DataSet disposal — verified non-disposable (v2, closed Phase 6)
`HistorianDataService` creates many `Proficy.Historian.ClientAccess.API.DataSet` instances
via `new DataSet()` without `using` blocks. Per GE's Historian 2022/2023 API documentation,
`DataSet` inherits directly from `System.Collections.Generic.Dictionary<string,IDataSamples>`
and adds no interfaces — so it is **not `IDisposable`**. No `using` blocks needed; no leak.
Source: GE Digital Historian ClientAccess API reference — DataSet class inheritance chain
is `Object → Dictionary<TKey,TValue> → DataSet`.
