---
description: Phase completion status and next items for v2 development
---

# Roadmap

## Status Legend
- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete

---

## Phase 1 — Service Layer Extraction
Extract domain logic out of `Main.cs` into independently testable classes.

- [x] Create `HistorianConnectionService`
  - Owns both `ServerConnection` instances
  - Exposes `ConnectPrimary(host)`, `ConnectSecondary(host)`, `IsConnected(side)`
  - Implements `IDisposable`, calls `Disconnect()` on dispose
- [x] Create `HistorianDataService`
  - All `IData` and `ITags` calls move here
  - Returns plain C# types (not Historian API types) where possible
  - No `Form` or `Control` dependencies
- [x] Create `GapAnalysisService`
  - `AnalyzeGaps(samples) → GapAnalysisResult`
  - `PlanBatches(gapWindows, batchSize) → List<GapBatch>`
  - `CheckBackfillFeasibility(batch, otherServerSamples) → bool`
- [x] Move model classes to `Models/` folder
  - `GapAnalysisResult.cs`, `GapWindow.cs`, `GapBatch.cs`
- [x] Slim `Main.cs` to UI wiring only (event handlers delegate to services)

---

## Phase 2 — Selective Synchronization
Replace full-range copy with gap-targeted, backfill-only writes.

- [x] Only write batches where `CanBackfill = true`
- [x] Preserve original sample quality (do not force `DataQuality.Good`)
- [x] Make `HistSync` tag name configurable (app.config + Properties.Settings)
- [x] Make batch size configurable (default: 10 minutes, read from app.config)
- [x] Add per-batch retry with exponential backoff (max 3 attempts)
- [x] Surface run-report: gaps found, batches attempted, batches succeeded, samples written

---

## Phase 3 — Read-After-Write Verification
Confirm data was actually written before marking a batch as done.

- [x] After each batch write, re-query the target for that time range
- [x] Compare returned sample count to written sample count
- [x] Log verification result per batch (pass / mismatch / no data)
- [x] Mark batch `Verified` or `VerificationFailed` in run-report

---

## Phase 4 — Automated Tests
Make core logic testable without the UI or a live Historian instance.

- [x] Add a test project (`HistorianSyncTool.Tests`)
- [x] Unit test `GapAnalysisService` with synthetic sample lists
  - Median interval calculation
  - Gap detection at 1.5× threshold
  - Batch boundary correctness (half-open interval)
  - Coverage ratio calculation
  - Backfill feasibility marking
  - Sort validation on unsorted input
- [x] Unit test `HistorianDataService` via helper extraction (Proficy-free seams):
  - `RetryHelper` — pure retry policy (success on first try, failures, exponential
    backoff timing, exception wrapping, argument validation)
  - `SampleFilter` — parse `object → float`, half-open `[start, end)` clipping,
    null/unparseable drop, early break on out-of-range time
  - `SampleBucketer` — sample grouping behaviour (extracted from `MainForm`)
- [ ] Integration test harness (optional): requires live Historian, tagged as `[Integration]`

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
- [ ] Combined bidirectional Preview window — P→S and S→P "will copy" in one dialog
- [ ] Clearer gap/tag table — "Server X has A→B that Y lacks → copy to Y", not raw batch counts
- [ ] System tray icon (still deferred)

---

## Bugs Fixed vs v1 (track here as each is resolved)

| Bug | Phase Fixed | Notes |
|-----|-------------|-------|
| ServerConnection never disposed | 1 | Fixed in `HistorianConnectionService.Dispose()` |
| Log uses dtTimestamp instead of DateTime.Now | 1 | Fixed in new `Log()` helper |
| HasSampleInRange strict boundary exclusion | 2 | Use `>=` start, `<` end — binary search in MarkBackfillFeasibility |
| TagQueryParams not reset between servers in stats | 1 | New params instance per server |
| All ops on UI thread | 1 | async/await + Task.Run in service calls |
| No retry on API calls | 2 | Retry helper in HistorianDataService (configurable via app.config) |
| HistSync tag name hardcoded | 2 | Moved to app.config `SyncTagName` |
| Batch size hardcoded (10 min) | 2 | Moved to app.config `BatchSizeMinutes` |
| Quality forced to Good on backfill writes | 2 | `WriteFloatSamplesWithQuality` preserves source quality |
| Bare catch blocks in disconnect | audit | Added Trace.TraceWarning logging |
| SetBusy re-entrancy (buttons not disabled) | audit | Action buttons disabled during ops |
| ReadRawInRange missing quality | audit | Now returns `(Time, Value, Quality)` tuple |
| Unsorted input to GapAnalysisService | audit | Auto-sorts if needed |
| Coverage wildly wrong on bimodal tags | 5 | HistSync-only gap analysis eliminates false gaps from irregular tag intervals |
| Radio buttons confused gap analysis scope | 5 | Removed — always use HistSync |
| DateTimePicker showed arrows not calendar | 5 | `ShowUpDown = false` |
| Gap grid Duration column cut off | 5 | `AutoSizeColumnsMode = Fill` |
| No UI refresh after backfill | 5 | `AutoRefreshAfterBackfill()` re-runs gap analysis |
| Single-tag backfill too limiting | 5 | Multi-tag via `TagSelectionDialog` |
| Data grids stayed stale after backfill | 6 | `AutoRefreshAfterBackfill` also re-reads primary/secondary grids |
| Verify-write failure counted as success | 6 | `BatchesSucceeded++` now gated on both `writeOk` AND `verifyOk` |
| CancellationTokenSource leaked per op | 6 | New `ResetCts()` helper disposes before re-allocating |
| Gap grid columns cramped in right panel | 6 | Reduced to 3 cols; hover tooltip surfaces full detail instead |
| No pre-flight stats before backfill | 6 | `TagSelectionDialog` now shows per-tag source/target/batch stats |
| No detailed post-backfill summary | 6 | New `SyncReportDialog` with per-tag grid + CSV/TXT export |
| Empty-server gap detection blocked backfill | 6 | `Analyze` emits whole-period gap when sample count is 0 |
| Deadband tags produced hundreds of false gaps | 6 | `MinimumGapSeconds` floor (default 120s) in threshold calc |
| Isolated missing samples below gap floor never copied | 6 | Backfill now uses direct timestamp comparison, not gap windows |
| Verify false-negative from sub-second rounding | 6 | Widened verify window to ±1s (Historian stores at second precision) |
| Backfill journal never saved (silent serialization crash) | 8 | `RevertedLocal` made nullable — `DateTime.MinValue`→UTC crashed `DataContractJsonSerializer` in UTC+ zones |
| Backfill re-copies samples forever; false "succeeded" | 8 | Diff + verify now whole-second (`SampleFilter.ToSecondTicks`); verify confirms each written second present |
