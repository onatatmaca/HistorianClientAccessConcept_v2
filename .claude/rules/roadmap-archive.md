---
description: Completed v2 phases 1-7 (archived from roadmap.md to keep it under 200 lines)
---

# Roadmap — Archive (Phases 1–7, all complete)

Split out of [`roadmap.md`](roadmap.md) once it passed the 200-line budget. Current phases
(8+) live there. Bug-tracking tables are in [`known-issues-archive.md`](known-issues-archive.md).

---

## Phases 1–4 — Foundations (condensed)
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

> **Note (Phase 11, 2026-07-16):** Phase 7's scheduler had a latent defect — its rolling
> window `[DateTime.Now - H, DateTime.Now]` mixed local wall-clock with the API's UTC frame,
> so at H ≤ 2 h it silently evaluated the future and backfilled nothing. Fixed by the
> API-boundary conversion; see [`known-issues.md`](known-issues.md).
