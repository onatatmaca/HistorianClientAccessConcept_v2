---
description: Phase completion status and next items for v2 development
---

# Roadmap

## Status Legend
- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete

---

## Phases 1–4 — Foundations (all complete; condensed)
- [x] **P1 Service layer**: `HistorianConnectionService` (IDisposable),
      `HistorianDataService`, `GapAnalysisService`, models to `Models/`, slim form
- [x] **P2 Selective sync**: feasibility-gated writes, quality preserved, configurable
      HistSync tag + batch size, retry w/ backoff, run-report
- [x] **P3 Read-after-write verification**: per-batch re-query + verified/failed status
- [x] **P4 Tests**: `HistorianSyncTool.Tests` — GapAnalysisService, RetryHelper,
      SampleFilter, SampleBucketer
- [ ] Integration test harness (optional): requires live Historian, tagged `[Integration]`

---

## Phase 5 — HistSync-as-Master & Multi-Tag Backfill
Redesign gap analysis to always use HistSync and support multi-tag backfill.

- [x] Remove radio buttons (HistSync vs Selected Tag) — always use HistSync
- [x] Fix coverage values (bimodal data false gaps eliminated by HistSync-only analysis)
- [x] Multi-tag backfill via `TagSelectionDialog` (CheckedListBox, shared tags only)
- [x] Per-tag + overall progress display during backfill
- [x] Auto-refresh gap analysis + coverage bars after backfill completes
- [x] Extend `SyncRunReport` with per-tag `TagBackfillResult` tracking
- [x] Fix DateTimePicker: calendar dropdown instead of up/down arrows
- [x] Expand quick-select buttons: 8 presets (1h, 6h, 24h, 3d, 7d, 30d, 90d, 1y) in 4×2 grid
- [x] Fix gap grid column overflow (`AutoSizeColumnsMode = Fill`)
- [x] Cross-thread safety audit (all UI values captured before `Task.Run`)

---

## Phase 6 — Polish, Interactivity & Backfill Transparency
Right-panel polish, interactive timeline, VIEW/BACKFILL button groups, richer dialogs.

- [x] Right panel: symmetric padding, taller coverage bars (40px) + per-bar tag label,
      interactive `CoverageBar` (hover tooltip, min 3px gaps), gap grid trimmed to 3 cols
- [x] Auto-run gap analysis (500ms debounce) on date OR tag change; per-tag coverage
      (HistSync fallback); empty-server side renders as red 0% bar
- [x] Middle column split into VIEW + BACKFILL groups; new `FlatButtonStyle.Info` (teal)
- [x] `TagSelectionDialog` per-tag pre-flight stats; new `SyncReportDialog` post-run
      summary with per-tag grid + CSV/TXT export
- [x] Audit fixes: `ResetCts` (CTS leak), verify-failure counted as failure, data grids
      auto-refresh after backfill (`RefreshLoadedGrids` / `AutoRefreshAfterBackfill`)
- [x] `MinimumGapSeconds` floor (app.config, default 120s) suppresses deadband false gaps;
      empty-server whole-period gap emitted so backfill can target it
- [x] **Backfill switched to direct timestamp comparison** (decoupled from gap windows) —
      catches isolated missing samples interval-based detection misses
- [x] Verify-write window widened to ±1s (Historian's second-level precision was causing
      false-negative verify reads at the `AddTicks(1)` boundary)
- [x] Docs split (`known-issues.md` v2 + `known-issues-v1.md`); legacy batch-based backfill
      preserved under `_backup/`; DataSet verified non-`IDisposable` (no `using` needed)

---

## Phase 7 — Scheduling
- [x] Settings page (`SchedulerSettingsDialog`): enable/disable, interval (min),
      eval window (h), direction (P→S / S→P / Both), tag-name mask, run-on-startup
- [x] Persistence: 7 new `Properties.Settings` keys (`ScheduleEnabled`,
      `ScheduleIntervalMinutes`, `ScheduleEvalWindowHours`, `ScheduleDirection`,
      `ScheduleTagFilter`, `ScheduleRunOnStartup`, `ScheduleLastRunUtc`)
- [x] `ScheduleService` — 15s poll timer, gating, next-run computation, run-now
      trigger, status-changed event for UI binding
- [x] Unattended backfill path: `ExecuteBackfill` takes `evalFromOverride`,
      `evalToOverride`, `unattended` params. In unattended mode the modal
      `SyncReportDialog` is suppressed and a one-line audit entry is appended to
      `logs/schedule-YYYY-MM.log` instead
- [x] `RunScheduledBackfillAsync` — headless: rolling window from now, browses
      shared-tag intersection with mask, runs configured direction(s)
- [x] Status-bar `lblSchedule` indicator: "Schedule: off" / "Next run: HH:mm" /
      "Schedule: running…", clickable → opens settings dialog
- [x] `ScheduleLogger` — monthly rolling file logger
      (`logs/schedule-YYYY-MM.log`); thread-safe, swallows IO errors
- [ ] System tray icon (deferred — not strictly required for unattended runs)

---

## Phase 8 — UX polish & backfill safety
- [x] Searchable tag dropdowns — `cboPrimary`/`cboSecondary` are editable
      `DropDown` + `AutoCompleteMode.SuggestAppend` with a CustomSource rebuilt
      from browsed names; type to filter hundreds of tags
- [x] Removed the "WRITE DATA | MULTIFIELD TAGS" section (manual single-sample
      entry is useless at BGA scale). `HistorianDataService` write methods kept.
- [x] **Revert / undo backfill**: every run journals the exact (tag, timestamp)
      pairs it wrote+verified to `logs/backfill-journal/{id}.json`. New
      `BackfillHistoryDialog` (opened via "Backfill History…" in the BACKFILL
      group) lists past runs; reverting deletes exactly those timestamps via
      `IData.Delete` so pre-existing data is never touched.
  - Double-guarded: red revert button disabled until an "Enable revert"
    checkbox is ticked AND a final confirm dialog (defaults to No) is accepted
  - Target matched by recorded hostname; must be connected. Entry marked
    `Reverted` only on a fully clean pass — errors keep it Active for safe retry
- [x] Scheduler optional manual tag multiselect — `SchedulerSettingsDialog` adds
      a "specific tags" mode (CheckedListBox of shared tags) alongside the mask;
      persisted via `ScheduleUseTagList` + `ScheduleTagList`
- [x] Combined bidirectional Preview window — `BidirectionalBackfillDialog`: P→S and
      S→P checklists side by side, per-tag "Will copy" via whole-second diff, single
      combined report after running both directions
- [x] Clearer gap/tag table — right-panel table is now the cross-server diff per
      direction ("Missing on / Count / Period" + full-sentence tooltips; Phase 9 added
      click-to-zoom)
- [ ] System tray icon (still deferred)

---

## Phase 9 — Boss feedback: findability, transparency, one progress surface
All four issues raised in the 2026-07 review. Verified by screenshot on every surface.

- [x] **SYNC TIMELINE** — full-width dual-track `GapTimeline` control (center top):
      Primary + Secondary on one shared axis, adaptive date ticks, hover crosshair,
      legend; red = other server has it, gray = missing on BOTH (unfillable),
      blue band = backfilled by this tool (journal-driven, disappears on revert),
      amber strip = copy candidates (whole-second diff, same as backfill)
- [x] Click-to-zoom: any timeline segment or table row zooms the date range to it
      (± 10 % padding); "⟲ zoom back" restores previous ranges (zoom stack)
- [x] **Linked tag selection** — picking a tag auto-selects the same tag on the other
      server (both directions), toggleable + persisted (`TagLinkEnabled`)
- [x] **Modal progress dialog** — single Cancel (ESC works), delayed-show 400 ms,
      per-tag + per-batch bars, elapsed time (no fake ETA); removed the status-bar
      progress bar + Cancel and the separate Stop button; main window blocked during
      operations; scheduled runs stay headless (`_suppressOpDialog`)
- [x] Backfill cancel → stop at batch boundary, journal what was written, ask
      **keep / revert now** (default keep)
- [x] **Live-edge guard** — every write path clamps eval end to now −
      `LiveEdgeGraceSeconds` (default 120 s): kills the "we can backfill forever"
      loop caused by in-flight samples near now; analysis display not clamped
- [x] Per-tag mini timeline in BOTH preview dialogs (click a tag row → coverage of
      both servers + copy candidates for that tag)
- [x] `SafeReadTimes` perf: `ReadRawInRange` instead of read-to-end-of-archive
- [x] `IntervalBuilder` service (MergePoints/Complement/Intersect/CoverageIntervals/
      SplitByFeasibility/BuildCopyableSegments) + `IntervalBuilderTests` (17 tests)
- [x] Optional app.config Historian login (`HistorianUsername`/`HistorianPassword`)
      for remote servers that reject empty usernames
- [x] **IP + port support** — server fields accept `host[:port]` / `ip[:port]`
      (`HostInputParser`; ports via `HistorianAddress.TcpPort`, IPs via
      `ProficyEndpoint.PrepareForIp` lenient-identity path — TLS/cert mode
      unchanged). Verified live against both test servers by IP with real data
- [x] Docs restructured: `scheduling-and-revert.md` + `known-issues-archive.md`
      split out to keep all rule files under 200 lines

---

## Phase 10 — Cadence-aware calculation system (2026-07 audit)
Root-caused live: 41% coverage on healthy deadband tags + phantom diffs on
independently-collecting redundant servers (see `known-issues.md`, measured on Genthin).

- [x] Per-tag gap rule `max(p90(intervals) × GapThresholdMultiplier, MinimumGapSeconds)`
      replaces median×1.5; rule shown on each timeline track ("gap rule: silence > 1h")
- [x] **`SyncPlanner`** — single source of truth for "what would a backfill copy":
      auto-detects aligned streams (exact-second diff, catches isolated misses) vs
      independent collectors (fills only real target outages — no phantom duplicates);
      wired into ExecuteBackfill, both previews, amber strip, missing-data table
- [x] `ReadRawInRange` → bounded `RawByNumberQuery` chunks (stops at range end; never
      abandons a server pagination cursor)
- [x] Date/time edits + quick presets no longer auto-run analysis (only Analyze Gaps
      button / zoom); tag changes still auto-analyze
- [x] Sync Report shows Started/Finished/Duration; journal stores `CompletedLocal`;
      Backfill History gained a Duration column
- [x] `SyncPlannerTests` (14 tests) + 16-check local harness; verified live on
      TEMP_02_WS (41% → 100%/100%, "In sync") and full 78-tag preview
- [ ] Live write acceptance: run one real backfill + revert via the new planner

---

## Bugs Fixed vs v1
Tracking table moved to [`known-issues-archive.md`](known-issues-archive.md)
(kept growing past this file's 200-line budget).
