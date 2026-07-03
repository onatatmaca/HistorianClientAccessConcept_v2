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

## UI Layer (WinForms) — Phase 9 layout
- Main form: `Forms/MainForm` (+ dialogs: TagSelection, BidirectionalBackfill,
  SyncReport, BackfillHistory, SchedulerSettings, Progress)
- Layout: left sidebar (connection / evaluation period / tags with link toggle),
  **center top = full-width SYNC TIMELINE** (`GapTimeline`: both servers on one shared
  time axis + copy-candidates strip + click-to-zoom), center = data grids + VIEW/BACKFILL
  action column, collapsible activity log; **right panel = MISSING DATA** (per-direction
  summary + cross-server diff table, row-click zooms the timeline)
- Every long operation runs behind the modal `ProgressDialog` (single Cancel, 400 ms
  delayed show); there is no status-bar progress bar or separate Stop button anymore
- Status feedback: `lblStatus` (status bar) + `txtLog` (activity log); both also feed
  the progress dialog's detail line while an operation runs
- **Do not use WPF.** v1 had dead WPF files (`MainWindow.xaml`, `App.xaml`) — v2 must not include them.

## v1 Problem: Monolithic Main.cs
In v1, `Main.cs` (1402 lines) mixed UI events, Historian API calls, and domain logic in one class.
This made the synchronization algorithm untestable without the UI.

## v2 Target: Service Layer
Extract these three services from the form class:

| Service | Responsibility |
|---|---|
| `HistorianConnectionService` | Open/close `ServerConnection`, optional config login, connection state |
| `HistorianDataService` | All `IData` and `ITags` queries and writes (retry-wrapped) |
| `GapAnalysisService` | Gap detection (per-tag p90 rule), batch planning, feasibility marking |
| `SyncPlanner` | THE definition of "what would a backfill copy" (Phase 10): per-tag aligned-vs-independent stream detection, outage-window planning; shared by backfill, previews, timeline strip and table (tested) |
| `IntervalBuilder` | Pure interval math for the timeline: merge points→ranges, percentile cadence, complement, intersect (tested) |
| `ScheduleService` / `ScheduleLogger` / `BackfillJournalService` | Unattended runs, audit log, revert journal |

The form should only wire UI events and delegate to services. Services must have no `Form`/`Control`
dependencies so they can be unit tested independently.

## Data Flow per Use Case

### Connect
1. User enters both servers (persisted): `host`, `host:port`, `ip` or `ip:port`
   (`HostInputParser`). IPs are handled by `ProficyEndpoint.PrepareForIp` — lenient
   DNS-identity verifier, TLS + cert mode unchanged (see `historian-api.md`)
2. `HistorianConnectionService.Open` sets the port for the next connect and creates
   the `ServerConnection` (optional app.config login for remote clients)
3. Status reflected in status bar + log; progress dialog appears if it takes > 400 ms

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

### Per-Tag Gap Analysis (display only; Phase 9 feeds the SYNC TIMELINE)
1. `RunGapAnalysis` reads the tag SELECTED per side (`cboPrimary`/`cboSecondary`,
   normally mirrored by the tag link); falls back to `SyncTagName` if a combo is empty
2. `Analyze` per server: median interval, `threshold = max(median × 1.5, MinimumGapSeconds)`;
   empty server ⇒ one whole-period `GapWindow`; `CanBackfill` marked per batch from
   the OPPOSITE server's data
3. The worker also prepares all timeline data off-thread: fillable/unfillable segments
   (`IntervalBuilder.SplitByFeasibility`), copy-candidate runs (whole-second diff),
   and the journal-driven "backfilled by this tool" bands
4. Results drive the SYNC TIMELINE, the missing-data table and the summary — never
   backfill planning

### Multi-Tag Backfill — direct timestamp comparison (Phase 6)
Gap analysis is **not consulted** for backfill planning. That path was replaced because
the `MinimumGapSeconds` floor (needed for deadband noise) also hid isolated missing
samples. Instead:

1. User clicks a Copy button (`TagSelectionDialog`, one direction) or Preview & Backfill
   (`BidirectionalBackfillDialog`, both directions); both show per-tag "Will copy"
   counts from `SyncPlanner` (identical to what the backfill writes) + mini timeline
2. Evaluation end is clamped to `now − LiveEdgeGraceSeconds` on every write path
   (dialog stats AND the backfill use the same clamped range)
3. `ExecuteBackfill` per tag:
   a. Read source + target samples in `[evalFrom, evalTo]`
   b. Missing = `SyncPlanner.Plan(...)` — aligned streams → exact whole-second diff;
      independent collector streams → only real target outages (see `sync-workflow.md`)
   c. Group into buckets by `BatchSize` (`SampleBucketer.GroupByBucket`)
   d. Per bucket: write (quality preserved) + verify each written second is present
4. `BatchesSucceeded` gated on both `writeOk` AND `verifyOk`; written seconds journaled
5. Progress in the modal `ProgressDialog` (tag x/y + batch x/y); cancel stops at the
   batch boundary and asks keep-or-revert
6. `TagBackfillResult` per tag; `SyncRunReport` aggregates; modal `SyncReportDialog`
   with CSV/TXT export (suppressed for unattended runs)
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
Scheduled runs are fully headless (`_suppressOpDialog` — no modal progress dialog).
Details: [`scheduling-and-revert.md`](scheduling-and-revert.md).
