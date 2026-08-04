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

## Centre = two cards: overview ⇄ one point (Phase 12b/12c)
- **Card 1, the landing screen** — every shared measurement point, one row each, both servers'
  completeness bars on a shared axis, worst first. Built by `Services/CoverageScanner` on top of
  `HistorianDataService.ReadBucketCounts` (`CalculatedQuery(Count)`: per-bucket sample counts for
  many points in ONE round-trip). Hard 10 s budget; unreached points stay "not checked yet" with
  a *Check the rest* button — never silently truncated.
- **Card 2, one point** — the zoomable `GapTimeline`, the `ValueChart` (both servers overlaid,
  missing periods shaded), and the two scrollable data tables. `‹ All measurement points` returns.
- **Estimate vs exact — do not blur this.** The overview is an ESTIMATE and always a LOWER
  BOUND. Two signals, both from the counts already fetched: an outage run only counts when it
  is longer than the lacking server's own typical spacing, and a shared-segment count shortfall
  only counts when both servers fill ≥ 80 % of segments at comparable rates. Counting every
  one-sided segment instead fabricated alarms on real data (228 where a restore would copy 0 —
  see `known-issues.md` for the measured table). Opening a point recomputes with `SyncPlanner`,
  which alone decides what a restore writes; the scan never feeds a write path. Marked "~".
- **Resolution is part of the truth.** A gap SHORTER than one bar segment cannot appear at all.
  Measured live (2026-08-04, TESTSV1/PC2, 273 points): a 13-day window flagged **251** points,
  the same servers over 365 days flagged only **201** — the ~50 real gaps were shorter than a
  22 h segment. The summary line therefore always prints "each segment ≈ …".
- **Measured cost** (same run): the count query is *cheaper than reading the data* — 0.04 s for
  200 buckets vs 0.17 s for the equivalent raw read, and its total matched the raw sample count
  exactly (10,080). Full-list scans: 13 days ≈ **2.3 s**, 365 days ≈ **40 s**. Per-query cost
  scales with how much archive the server must walk, not with the number of round trips, so a
  long window is inherently slow — that is what the 10 s budget + "Check the rest" exists for.
- **Ordering** is by `PointCoverage.SortRank`: restorable gaps, then read failures, then points
  configured on one server only, then unchecked, then healthy. One-sided points must NOT lead —
  on the live rig 201 of 273 points exist on one server only (a migration left them on PC2) and
  ranking them first buried every actionable gap.
- The right-hand panel always belongs to the visible card (list totals vs that point's exact
  numbers) — `ShowOverview`/`RunGapAnalysis` each refresh it.

## View modes, language and demo mode (Phase 12a)
- **Simple is the product, Advanced only ADDS.** `Settings.AdvancedMode` (default off) toggles
  the technical surface: filter box, Server statistics, point link + mirror selector, activity
  log, Compare/Link-scrolling, per-direction copy buttons, batch counters, gap rule on the
  timeline. `MainForm.ApplyViewMode()` is the only place that decides. Nothing is deleted, so a
  diagnostic view used during an acceptance test is one click away.
- Anything hidden must keep a working default — the tag mask falls back to `*`
  (`EffectiveMask()`), the point link is forced on. **Never read state off a control the view
  mode can hide**: a hidden ComboBox reports no selection at all (see `known-issues.md`), which
  is why the selected point lives in `_pointPrimary` / `_pointSecondary`.
- **Language**: `UI/Loc.cs` holds every user-visible string EN+DE and raises `LanguageChanged`;
  `MainForm.Designer.ApplyTexts()` assigns them in ONE place, `MainForm.ApplyLanguage()` also
  re-runs the analysis so strings produced during a run are not left in the old language.
- **Server naming**: `Services/ServerNaming` turns the internal labels into "HOST — main server"
  / "HOST — mirror". The internal `"Primary"`/`"Secondary"` strings are load-bearing (journal
  entries, `ScheduleDirection`, `ExecuteBackfill` branching) — **rename on screen, never in
  storage**, or old runs can no longer be reverted.
- **Demo mode** (`--demo`): `DemoDataService : HistorianDataService` overrides every API method
  and never calls `base`, and `HistorianConnectionService.EnableDemoMode` hands out two
  never-connected sentinel connections. A demo session therefore cannot touch a server. Used for
  screenshot verification and for showing the tool without a Historian; an amber banner makes it
  unmistakable.

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
| `HistorianDataService` | All `IData` and `ITags` queries and writes (retry-wrapped). **THE time-frame boundary** — see below |
| `GapAnalysisService` | Gap detection (per-tag p90 rule), batch planning, feasibility marking |
| `SyncPlanner` | THE definition of "what would a backfill copy" (Phase 10): per-tag aligned-vs-independent stream detection, outage-window planning; shared by backfill, previews, timeline strip and table (tested) |
| `IntervalBuilder` | Pure interval math for the timeline: merge points→ranges, percentile cadence, complement, intersect (tested) |
| `ScheduleService` / `ScheduleLogger` / `BackfillJournalService` | Unattended runs, audit log, revert journal |

The form should only wire UI events and delegate to services. Services must have no `Form`/`Control`
dependencies so they can be unit tested independently.

## Time frames — one boundary, no exceptions (Phase 11)
The Historian API works in **UTC**; the **rest of the app works in LOCAL time** (date pickers,
`DateTime.Now`, every display). `HistorianDataService.ToApi()` / `FromApi()` is the **single**
conversion point — applied to query bounds, returned times, writes and deletes.

**Do not convert anywhere else, and never mix the frames.** .NET compares raw `Ticks` and ignores
`DateTimeKind`, so a `DateTime.Now` compared against an API timestamp is silently wrong by the UTC
offset (1 h winter / 2 h summer). That single mistake previously made the live-edge guard a dead
no-op, let the scheduler evaluate the future, made "Last 1h" return nothing, and shifted every
evaluation window by an hour. Keeping the boundary in one place is what makes those correct *by
construction*. Details + the proof: [`historian-api.md`](historian-api.md).

**Exception — the backfill journal is stored in UTC ticks** (`long[]`, never `DateTime`), so legacy
and new entries revert identically. `RevertBackfill` re-tags them `Kind=Utc` on the way out. Do not
"normalise" them to local: old reverts would then delete at an instant 1–2 h off, i.e. real data.

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
2. `Analyze` per server: per-tag rule `threshold = max(p90(intervals) × GapThresholdMultiplier,
   MinimumGapSeconds)` (Phase 10 — NOT median × 1.5, which collapsed coverage on deadband
   tags); empty server ⇒ one whole-period `GapWindow`; `CanBackfill` marked per batch from
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
