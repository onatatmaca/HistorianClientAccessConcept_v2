---
description: Gap analysis algorithm, HistSync tag behavior, backfill workflow, and batch planning
---

# Sync Workflow

## The HistSync Tag
A special float tag named `HistSync` must exist on both servers. It acts as a heartbeat:
a collector writes to it at a regular interval. The gap analysis detects when that heartbeat
stopped, which implies a data collection outage on that server.

The tag name is configurable via `app.config` (`SyncTagName` setting, default `"HistSync"`).
Gap analysis **always** uses this tag — there is no per-tag gap analysis option.

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

1. Fetch all raw samples of `HistSync` via `RawByTimeQuery`
2. Compute all consecutive time deltas between samples
3. **Expected interval = median of all deltas** (robust to outliers)
4. A gap is any consecutive pair where `delta > 1.5 × expectedInterval`
5. Collect all such pairs as `GapWindow` objects

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

## Backfill Execution: ExecuteBackfill (Multi-Tag)

```
for each tag in tagsToBackfill:          // user-selected via TagSelectionDialog
    for each batch in gapResult.Batches: // from HistSync gap windows
        if batch.CanBackfill:
            ReadRawInRange(sourceConn, tag, batch.Start, batch.End)
            → WriteFloatSamplesWithQuality (preserves original quality)
            → VerifyWrite (read-after-write check)
            → track in TagBackfillResult
```

Key design: HistSync defines WHERE gaps are. The user chooses WHICH tags to copy into those gaps.
Tags with no source data in a gap window are silently skipped.

## Tag Selection Dialog (`Forms/TagSelectionDialog.cs`)
- Shows tags that exist on BOTH servers (intersection)
- `CheckedListBox` with Select All / Select None buttons
- Summary header shows gap count and backfillable batch count
- Shown before every backfill operation (Copy to Primary, Copy to Secondary, Preview & Backfill)

## State Dependencies
The Copy/Backfill buttons depend on gap analysis having run first:
- `_lastPrimaryResult` / `_lastSecondaryResult` must be non-null with gaps
- Both servers must be connected for backfill

After backfill → `AutoRefreshAfterBackfill` re-runs `RunGapAnalysis` and updates coverage bars.

## v2 Improvements — All Complete
- [x] HistSync tag name configurable (`SyncTagName` in app.config)
- [x] Batch size configurable (`BatchSizeMinutes` in app.config, default 10)
- [x] Original sample quality preserved (`WriteFloatSamplesWithQuality`)
- [x] Read-after-write verification per batch
- [x] Per-batch retry with exponential backoff (3 attempts)
- [x] Run-report summary (`SyncRunReport` + per-tag `TagBackfillResult`)
- [x] Multi-tag backfill via `TagSelectionDialog`
- [x] Auto-refresh after backfill
