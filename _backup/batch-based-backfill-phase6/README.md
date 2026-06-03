# Backup: Batch-based (gap-window) backfill — phase 6 snapshot

This folder preserves the **pre-direct-comparison** backfill implementation in case we need
to revert. Files here are NOT compiled (`.txt` extension, folder not in `.csproj`). They're
reference copies of the full `MainForm.cs` and `TagSelectionDialog.cs` as they existed at
the last commit where batch-based backfill was the active code path.

## Snapshot info

| Item | Value |
|---|---|
| Snapshot commit | `f73d8be` — "Minimum-gap-duration floor to suppress deadband false gaps" |
| Date | 2026-04-24 |
| Commit that replaced this code | `fe8f56e` — "Backfill now uses direct timestamp comparison (not gap windows)" |

## What the old approach did

1. `RunGapAnalysis` reads both servers, detects gaps by interval (median × 1.5, floor 120s)
2. `GapAnalysisService.PlanBatches` splits each gap into 10-minute `GapBatch` objects
3. `MarkBackfillFeasibility` tags each batch `CanBackfill=true` if the opposite server has any sample in `[batch.Start, batch.End)`
4. `ShowTagSelectionDialog` gate: aborts if zero batches are marked backfillable
5. `ExecuteBackfill` per tag: loop over all CanBackfill batches; for each, read source in the batch range, write to target, verify

## Why we moved away from it

The floor protection against deadband noise (`MinimumGapSeconds = 120`) ALSO hid real
isolated missing samples (20-second mini-gaps where Secondary was missing one sample while
Primary had it). Interval-based gap detection couldn't tell noise from data-loss, so
backfill silently skipped those missing samples. Direct timestamp comparison in the new
code compares actual sample timestamps between the two servers and copies exactly what's
missing — no interval heuristic at all for the backfill path.

## When you might want to revert

- If direct comparison turns out to be too slow on very long evaluation ranges (it reads
  both servers' full ranges per tag, up-front)
- If memory pressure becomes a problem (both sample lists + HashSet held in RAM during diff)
- If you want batch structure preserved for a different integration (e.g., resumable runs
  tied to fixed 10-minute windows)

## How to revert

**Option A — cherry-pick the revert** (recommended):

```bash
git revert fe8f56e  # reverts the "direct comparison" commit
# resolve any merge conflicts from later patches (e3d78f4 verify-fix will conflict; keep
# the ±1s verify window since it's valid in either approach)
```

**Option B — manual restore**:

1. Open `_MainForm.cs.txt` and copy the `ExecuteBackfill`, `btnBackfillPreview_Click`,
   and `ShowTagSelectionDialog` methods back into `Forms/MainForm.cs`, replacing the
   current direct-comparison versions
2. Open `_TagSelectionDialog.cs.txt` and restore the seven-column grid layout
   (`Batches w/ data`, `Est. samples` columns) and the gap-batch-based `LoadTagStats`
3. Keep the ±1s verify window from `e3d78f4` — that bug applies in both architectures
4. Build; resolve any call-site mismatches
5. Update `sync-workflow.md` and `known-issues.md` to reflect the revert

**Option C — full-file replace** (nuclear):

```bash
cp _backup/batch-based-backfill-phase6/_MainForm.cs.txt Forms/MainForm.cs
cp _backup/batch-based-backfill-phase6/_TagSelectionDialog.cs.txt Forms/TagSelectionDialog.cs
# then manually re-apply the ±1s verify-window fix from e3d78f4
# build and test
```

## Hybrid (if we later want both)

The cleanest future would be a toggle in `app.config`:
```xml
<add key="BackfillStrategy" value="DirectComparison" />   <!-- or "GapWindows" -->
```

Neither path is deleted; the app picks at runtime. Would need a shared `IBackfillStrategy`
interface with two implementations. Not implemented yet — just noted here for the roadmap.
