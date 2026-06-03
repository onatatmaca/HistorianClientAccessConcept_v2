---
description: Gap analysis algorithm, HistSync tag behavior, backfill workflow, and batch planning
---

# Sync Workflow

## Source Tag Selection (per-tag as of Phase 6 update)
Gap analysis uses the **currently selected tag** on each side (`cboPrimary` / `cboSecondary`).
If a combo is empty, it falls back to the configured HistSync heartbeat tag (app.config
`SyncTagName`, default `"HistSync"`).

Primary and secondary can be analyzed against different tags. Backfill feasibility for a
gap is checked against the **same tag** on the opposite server — so you can only backfill
what actually exists on the source.

### Tradeoff (documented from Phase 5)
Per-tag analysis can show false gaps on bimodal sampling (e.g., pairs of samples 1s apart,
then 15s between pairs). The median-based detector picks 1s as the interval and flags every
normal 15s pause as a gap. If the user sees wild coverage numbers, switch the combo to
`HistSync` to see the authoritative server-uptime view.

## Domain Model

```csharp
class GapAnalysisResult
{
    bool     HasData;
    bool     HasGap;
    int      TotalSamples;
    DateTime FirstTimestamp;
    DateTime LastTimestamp;
    TimeSpan ExpectedInterval;   // computed via median of all deltas
    TimeSpan LargestDelta;
    TimeSpan MissingDuration;    // sum of all gap durations
    double   CoverageRatio;      // 0.0–1.0
    List<DateTime>  SampleTimes;
    List<GapWindow> Gaps;
}

class GapWindow
{
    DateTime       Start;
    DateTime       End;
    TimeSpan       Duration;
    List<GapBatch> Batches;
}

class GapBatch
{
    DateTime Start;
    DateTime End;
    TimeSpan Duration;
    bool     CanBackfill;  // true if the opposing server has data in this range
}
```

## Gap Detection Algorithm

1. Fetch all raw samples of the selected tag via `RawByTimeQuery`
2. Compute all consecutive time deltas between samples
3. **Expected interval = median of all deltas** (robust to outliers)
4. Compute `gapThreshold = max(expectedInterval × 1.5, MinimumGapSeconds)`
5. A gap is any consecutive pair where `delta > gapThreshold`
6. Collect all such pairs as `GapWindow` objects

### Why the `MinimumGapSeconds` floor exists
On deadband-logged tags (samples only written when the value changes), intervals vary
wildly — 1s during fast changes, 30s+ during stable periods. The median can shrink to a
few seconds, so `median × 1.5` would flag every normal quiet stretch as a gap. The floor
(default 120s, configurable in `app.config`) ignores any delta shorter than that, which
eliminates the noise and leaves only real outages visible.

If you still see tiny false gaps, raise `MinimumGapSeconds` in `app.config` (e.g., 300 for
five minutes). If the tag is a steady heartbeat like HistSync (~60s interval), leave the
default.

## Batch Planning

Each `GapWindow` is split into `GapBatch` objects of fixed 10-minute duration:
```
window.Start, window.Start + 10min, window.Start + 20min, ..., window.End
```
`CanBackfill` is set `true` when `HasSampleInRange(otherServerSamples, batch.Start, batch.End)`
returns `true`.

**v1 boundary bug:** `HasSampleInRange` used strict `>` and `<` which excluded samples exactly
on batch boundaries. In v2, use `>=` for start and `<` for end (half-open interval).

## Coverage Ratio
```
coverageRatio = (totalSpan - missingDuration) / totalSpan
```
Displayed as a colored progress bar: red = missing, green = covered.

## Backfill Execution: ExecuteBackfill (Direct Comparison)

As of Phase 6, backfill uses **direct timestamp comparison** between source and target —
NOT interval-based gap windows. This catches isolated missed samples that fall below the
`MinimumGapSeconds` floor (which is essential for noise suppression on deadband-logged
tags but would otherwise hide one-off missing samples).

```
for each tag in tagsToBackfill:
    srcData = ReadRawInRange(sourceConn, tag, evalFrom, evalTo)
    tgtData = ReadRawInRange(targetConn, tag, evalFrom, evalTo)
    missing = srcData where ToSecondTicks(Time) ∉ tgtData seconds  // whole-second diff
    if missing is empty → skip (already in sync)
    batches = group missing by time buckets of BatchSize (default 10 min)
    for each batch in batches:
        WriteFloatSamplesWithQuality(targetConn, tag, batch)
        VerifyWrite — re-read; confirm each written SECOND is present
        BatchesSucceeded++ iff writeOk && verifyOk
```

Key distinction: gap windows are used ONLY for the UI (coverage bars, gap grid — answering
"does this server have big outages?"). Backfill uses the actual per-sample diff between
servers to decide what to copy, so it is complete regardless of whether the gap-detection
threshold would have flagged the missing timestamps or not.

## Tag Selection Dialog (`Forms/TagSelectionDialog.cs`)
Shows tags that exist on BOTH servers (intersection) with per-tag pre-flight stats computed
by direct timestamp comparison on a background thread:

| Column          | Shows                                                        |
|-----------------|--------------------------------------------------------------|
| ✓ (checkbox)    | Selection state                                              |
| Tag             | Tag name                                                     |
| Source samples  | Count in source server across the evaluation range           |
| Target samples  | Count in target server across the evaluation range           |
| Will copy       | Source timestamps NOT present on target (whole-second diff)  |
| Write range     | First → last missing timestamp                               |

Tags with `Will copy = 0` (already in sync) are dimmed. Marquee progress bar tracks stat
loading; Backfill button disabled until stats complete. Dialog cancel aborts in-flight queries.

## Post-Backfill Report (`Forms/SyncReportDialog.cs`)
Modal dialog shown after every backfill (success OR failure). Displays:
- Header with source → target, duration, overall batches succeeded/attempted ratio
- Totals: gaps found, samples written, failed batches, tag count
- Per-tag grid: Attempted · Succeeded · Failed · Samples · Errors (full list via tooltip)
- Rows tinted pale red when all-failed, pale yellow when any errors
- Export buttons: CSV (semicolon-delimited, Excel-friendly) and TXT (columnar, ticket-friendly)

## State Dependencies
The Copy/Backfill buttons depend on gap analysis having run first:
- `_lastPrimaryResult` / `_lastSecondaryResult` must be non-null with gaps
- Both servers must be connected for backfill

After backfill → `AutoRefreshAfterBackfill`:
1. Exits compare mode if active (simpler than re-aligning)
2. Re-runs `RunGapAnalysis` — updates coverage bars + gap grid
3. Re-reads primary and/or secondary data grids IF they had data loaded AND servers are still connected

## Auto-analyze
Changing the date pickers (or quick-select presets) OR changing the tag selection in
`cboPrimary` / `cboSecondary` triggers a 500ms debounced auto-re-run of `RunGapAnalysis`.
Suppressed during `LoadSettings`, while busy, or when no server is connected.

**Scope**: auto-analyze updates the coverage bars + gap grid only. It does NOT re-read the
primary/secondary data grids (too chatty during date-picker dragging). The explicit
`Analyze Gaps` button DOES re-read them via `RefreshLoadedGrids()`, matching the behavior
of `AutoRefreshAfterBackfill`.

## Coverage Bar Interactivity (`UI/Controls/CoverageBar.cs`)
- Hovering a red segment shows a tooltip: tag label + start → end + duration
- Gaps narrower than 3px are widened to 3px so they remain hoverable
- Hovering the green area shows overall coverage %

## Unattended Scheduled Backfill (Phase 7)
`ScheduleService` runs `RunScheduledBackfillAsync` on a fixed interval. Click the
status-bar `lblSchedule` ("Schedule: off" / "Next run: HH:mm") to open
`SchedulerSettingsDialog` and configure:

| Setting              | Default | Notes                                         |
|----------------------|---------|-----------------------------------------------|
| ScheduleEnabled      | false   | Master toggle                                 |
| IntervalMinutes      | 60      | Time between runs                             |
| EvalWindowHours      | 24      | Rolling window: `[now - N, now]`              |
| Direction            | Both    | `PrimaryToSecondary` / `SecondaryToPrimary` / `Both` |
| TagFilter            | `*`     | Mask applied to the shared-tag intersection   |
| RunOnStartup         | false   | Trigger one run shortly after app launch      |

Each scheduled run is gated by `IsPrimaryConnected && IsSecondaryConnected && !_isBusy`,
writes a one-line audit entry to `{exe}/logs/schedule-YYYY-MM.log` (no modal popup), and
auto-refreshes the UI via `AutoRefreshAfterBackfill` afterward.

`SchedulerSettingsDialog` also offers a **manual tag multiselect** (radio: mask vs.
explicit list). When `ScheduleUseTagList` is set, `RunScheduledBackfillAsync` browses
`*`, intersects both servers, then narrows to `ScheduleTagList`.

## Revert / Undo a Backfill
Every backfill journals the exact `(tag, timestamp)` pairs it wrote+verified to
`logs/backfill-journal/{id}.json` (`BackfillJournalService`). The "Backfill
History…" button opens `BackfillHistoryDialog`; reverting deletes **only** those
timestamps via `HistorianDataService.DeleteSamples` → `IData.Delete(string[],
DateTime[], out ItemErrors)` (chunked 1000/call) — pre-existing samples are never
touched (worst case: incomplete revert, never a wrong deletion). Double-guarded
(enable-checkbox + confirm); target must be connected (matched by recorded hostname);
entry marked `Reverted` only on a clean pass, else kept Active for retry.
