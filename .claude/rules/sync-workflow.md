---
description: Gap analysis algorithm, HistSync tag behavior, backfill workflow, and batch planning
---

# Sync Workflow

## The HistSync Tag
A special float tag named `HistSync` must exist on both servers. It acts as a heartbeat:
a collector writes to it at a regular interval. The gap analysis detects when that heartbeat
stopped, which implies a data collection outage on that server.

The tag name is currently hardcoded as `"HistSync"`. In v2 this should be configurable.

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

## Backfill Execution: CopySamplesForHistSyncGaps

```
for each GapWindow in targetGapResult.Gaps:
    for each GapBatch in window.Batches:
        if batch.CanBackfill:
            ReadFloatSamplesInRange(sourceConn, sourceTag, batch.Start, batch.End)
            → write to targetConn via IData.Add(writeSet, false, out errors)
            → log per-batch result
```

`ReadFloatSamplesInRange` uses `RawByTimeQuery` from `batch.Start`, then filters returned
samples to `[start, end)` in a loop, parsing each value with `float.TryParse`.

**Important:** The copy currently writes with `ImplicitQuality = DataQuality.Good`. In v2,
preserve the original quality from the source sample.

## State Dependencies
The Copy buttons depend on gap analysis having run first:
- `On_cmdMoveToPrimary_Click` requires `lastPrimaryHistSyncGap != null`
- `On_cmdMoveToSecondary_Click` requires `lastSecondaryHistSyncGap != null`

If gap analysis has not run, the buttons abort early with an error message.

## v2 Improvements Needed
- [ ] Make `HistSync` tag name configurable (not hardcoded)
- [ ] Make batch size configurable (currently hardcoded 10 minutes)
- [ ] Preserve original sample quality during backfill (do not force `DataQuality.Good`)
- [ ] Add read-after-write verification (re-query target range after write, compare counts)
- [ ] Add per-batch retry with exponential backoff on write failure
- [ ] Surface a run-report summary (total gaps, batches attempted, batches succeeded, samples written)
