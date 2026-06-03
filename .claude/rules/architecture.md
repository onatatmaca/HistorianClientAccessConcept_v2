---
description: Overall application architecture, dual-server design, and v2 service-layer goals
---

# Architecture

## Dual-Server Model
The application always operates against two Historian instances:
- **Primary** (`sc`) — the authoritative data source
- **Secondary** (`scs`) — the redundant/mirror server

Both are `ServerConnection` objects. Most operations require both to be connected. The secondary
hostname is derived from the primary at startup: if the primary ends with `PC2`, the secondary
strips that suffix; otherwise `PC2` is appended.

## UI Layer (WinForms)
- Single form: `Main` (inherits `Form`)
- Entry point: `Program.cs` → `Application.Run(new Main())`
- All button click handlers are named `On_cmd<Action>_Click`
- Status feedback goes to `tsStatus` (status strip label) and `txt_Log` (textbox log)
- Grids: `dataGridDataPrimary`, `dataGridDataSecondary`, `dataGridCompare`
- Tag selectors: `cboTagsPrimary`, `cboTagsSecondary`
- **Do not use WPF.** v1 had dead WPF files (`MainWindow.xaml`, `App.xaml`) — v2 must not include them.

## v1 Problem: Monolithic Main.cs
In v1, `Main.cs` (1402 lines) mixed UI events, Historian API calls, and domain logic in one class.
This made the synchronization algorithm untestable without the UI.

## v2 Target: Service Layer
Extract these three services from the form class:

| Service | Responsibility |
|---|---|
| `HistorianConnectionService` | Open/close `ServerConnection`, expose connection state |
| `HistorianDataService` | All `IData` and `ITags` queries and writes |
| `GapAnalysisService` | Gap detection, batch planning, backfill orchestration |

The form should only wire UI events and delegate to services. Services must have no `Form`/`Control`
dependencies so they can be unit tested independently.

## Data Flow per Use Case

### Connect
1. User enters primary hostname → secondary derived automatically
2. `HistorianConnectionService` creates two `ServerConnection` instances
3. Status reflected in status strip + log

### Browse Tags
1. `ITags.Query` with `TagnameMask` filter and `DataType = Float`
2. Paginated with `PageSize = 100` using `while` loop
3. Results bound to comboboxes

### Read Samples
- Interpolated query: `now-10min` to `now`, 10 samples, selected tag
- Raw query: from `dtStartdate` to now, all samples

### Compare
1. Raw query both tags from same start date
2. Align by timestamp into `SortedDictionary<DateTime, CompareRowData>`
3. Missing side shown as `"missing"` in grid

### Per-Tag Gap Analysis (Phase 6 — display only)
1. `RunGapAnalysis` reads the tag SELECTED per side (`cboPrimary`/`cboSecondary`);
   falls back to `SyncTagName` (HistSync) if a combo is empty
2. `Analyze` per server: compute median interval, detect gaps with
   `threshold = max(median × 1.5, MinimumGapSeconds)` to suppress deadband noise
3. Empty-server (0 samples) case: emit one whole-period `GapWindow` so it shows as 0% red
4. Mark `CanBackfill` per batch by checking the OPPOSITE server has same-tag data
5. Results stored in `_lastPrimaryResult` / `_lastSecondaryResult` — drive the coverage
   bars, gap grid, and `SyncReportDialog` header stats only

### Multi-Tag Backfill — direct timestamp comparison (Phase 6)
Gap analysis is **not consulted** for backfill planning. That path was replaced because
the `MinimumGapSeconds` floor (needed for deadband noise) also hid isolated missing
samples. Instead:

1. Gap analysis must have detected gaps on the target side (precondition only)
2. User clicks Copy button → `TagSelectionDialog` shows tags on BOTH servers with
   per-tag "Will copy" counts from direct comparison
3. `ExecuteBackfill` per tag:
   a. Read source samples in `[evalFrom, evalTo]`
   b. Read target samples in same range
   c. Build `HashSet<long>` of target ticks; find source timestamps not in it
   d. Group missing samples into buckets by `BatchSize` (`SampleBucketer.GroupByBucket`)
   e. For each bucket: write + verify (verify window widened ±1s for Historian precision)
4. `BatchesSucceeded` gated on both `writeOk` AND `verifyOk`
5. Per-tag progress in status bar; progress bar resets per tag
6. `TagBackfillResult` tracks per-tag stats; `SyncRunReport` aggregates; modal
   `SyncReportDialog` pops up after each run with CSV/TXT export
7. `AutoRefreshAfterBackfill` re-runs gap analysis AND re-reads loaded data grids

The old gap-window-based implementation is preserved in `_backup/batch-based-backfill-phase6/`
for reference if we ever need to revert.

### Unattended Scheduled Backfill (Phase 7)
`ScheduleService` owns a 15s `System.Windows.Forms.Timer` that runs the configured
backfill on a fixed interval. Persisted settings live in `Properties.Settings`
(`ScheduleEnabled`, `ScheduleIntervalMinutes`, `ScheduleEvalWindowHours`,
`ScheduleDirection`, `ScheduleTagFilter`, `ScheduleRunOnStartup`,
`ScheduleLastRunUtc`).

`RunScheduledBackfillAsync` per tick:
1. Skip if servers are not both connected, or a manual operation is in progress
2. Compute rolling window: `[now - ScheduleEvalWindowHours, now]`
3. Browse tag intersection on both servers, apply `ScheduleTagFilter` mask
4. For each direction in `ScheduleDirection`, call `ExecuteBackfill(...unattended: true)`
5. Refresh on-screen UI via `AutoRefreshAfterBackfill`
6. Persist `ScheduleLastRunUtc = DateTime.UtcNow`

`ExecuteBackfill` accepts `evalFromOverride`/`evalToOverride`/`unattended`. In
unattended mode the modal `SyncReportDialog` is suppressed; instead `ScheduleLogger`
appends a single-line audit entry to `logs/schedule-YYYY-MM.log`.

UI surface: `lblSchedule` clickable status-bar label shows "Schedule: off" /
"Next run: HH:mm" / "Schedule: running…" and opens `SchedulerSettingsDialog`.
Tray icon was scoped out for v1 — the status-bar indicator covers the same need.
