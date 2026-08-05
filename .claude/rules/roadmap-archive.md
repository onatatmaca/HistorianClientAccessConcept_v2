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

## Phase 9 — Findability, transparency, one progress surface (2026-07 review)
- [x] **SYNC TIMELINE** — full-width dual-track `GapTimeline`: both servers on ONE shared axis;
      red = the other server has it, grey = missing on both, blue band = written by this tool
      (journal-driven, vanishes on revert), amber strip = exactly what a restore would copy
- [x] Click any segment or table row → zoom the range to it (±10 %); "⟲ zoom back" pops a stack
- [x] Linked point selection across both servers, toggleable, persisted (`TagLinkEnabled`)
- [x] ONE modal `ProgressDialog` (single Cancel, ESC, 400 ms delayed show); the status-bar
      progress bar and the separate Stop button are gone. Cancel mid-restore → journal what was
      written, then ask keep / revert now. Scheduled runs stay headless (`_suppressOpDialog`)
- [x] **Live-edge guard** — every write path clamps the end to `now − LiveEdgeGraceSeconds`,
      killing the "we can restore forever" loop caused by in-flight samples. Display not clamped
- [x] Per-point mini timeline in both preview dialogs; `IntervalBuilder` + 17 tests
- [x] **IP + port support** — `host[:port]` / `ip[:port]`; ports via `HistorianAddress.TcpPort`,
      IPs via `ProficyEndpoint.PrepareForIp` (lenient DNS identity, TLS/cert mode unchanged).
      Verified live against both test servers by IP with real data
- [x] Optional app.config login (`HistorianUsername`/`HistorianPassword`) for remote servers

## Phase 10 — Cadence-aware calculation (2026-07 audit)
Root-caused live on Genthin data: 41 % coverage on healthy deadband tags, and phantom diffs on
independently-collecting redundant servers.
- [x] Per-tag gap rule `max(p90(intervals) × multiplier, MinimumGapSeconds)` replaces
      median × 1.5; the rule is shown on each timeline track
- [x] **`SyncPlanner`** — the single definition of "what would a restore copy": aligned streams
      → exact whole-second diff (catches isolated misses); independent collectors → only real
      target outages (no phantom duplicates). Wired into the backfill, both previews, the amber
      strip and the missing-data table, so every surface reports identical numbers
- [x] `ReadRawInRange` → bounded `RawByNumberQuery` chunks; never abandons a server pagination
      cursor (an abandoned RawByTime cursor leaks: "Maximum number of cached items exceeded")
- [x] `SyncPlannerTests` (14) + a 16-check harness; verified live on TEMP_02_WS (41 % → 100 %)
- [x] **Live write acceptance**: write → read → journal → revert round-trip against TESTSV1PC2
      in BOTH DST seasons, 22/22 checks, incl. delete precision (revert one sample, the +10 s
      neighbour survives). Closes the revert path, which had never been live-verified

## Phase 11 — UTC/local frame fix (2026-07-16)
Root-caused after ~1 h of Secondary data was destroyed by an external helper tool (NOT the app —
its deletes are journal-driven). The audit then exposed a real bug class.
- [x] **Proven live**: the API frame is UTC, and the `DateTimeKind` of a query start shifts the
      query by the UTC offset (−59.2 min in Feb, −110.8 min in May for Local/Unspecified)
- [x] **`HistorianDataService.ToApi()/FromApi()`** — one boundary; everything above it is LOCAL
- [x] Fixed **by construction**: the dead live-edge guard, the scheduler's no-op at ≤2 h windows,
      the empty "Last 1h" preset, the 1–2 h window shift, the phantom trailing grey band
- [x] Reads `ThrowOnItemErrors` — an errored read must never look like "no data"
- [x] Journal frame deliberately UNCHANGED (UTC ticks) → legacy and new entries revert
      identically, no migration. **Do not convert journal ticks to local**
- [ ] Revert reports *requested* not *confirmed* deletions (`MainForm.cs`) — fails safe

## Phase 12a–12c — Customer-friendly UI (2026-08-04)
Boss review: customers do not understand *samples, batches, tags, backfill, gap rule*. 16
decisions captured with the user before implementation.
- [x] **12a** `UI/Loc.cs` holds every string EN+DE, switchable at runtime (no satellite
      assemblies — delivery stays one .exe); full plain-language pass over the main form and all
      six dialogs; header strip with `ServerNaming` ("HOST — main server ↔ HOST — mirror"), the
      EN|DE switch and the **Advanced** toggle (Advanced only ADDS; nothing is deleted);
      auto-connect on startup — which made the scheduler's "run on startup" reachable for the
      first time, so it now needs an explicit one-time confirmation; **offline `--demo` mode**
      (`DemoDataService` overrides every API method and never calls `base`, so a demo session
      cannot reach a server) used for all screenshot verification
- [x] **12b** `ReadBucketCounts` (`CalculatedQuery(Count)`, many points per round-trip) +
      `CoverageScanner` (chunks of 20, cancellable, **10 s budget**, "Check the rest" for what it
      did not reach) + `TagOverviewList` (one owner-drawn virtualised list, worst first,
      client-side search). Points on only ONE server are rows too — the list is the UNION — but
      are excluded from the restore totals. `PointCoverageTests` (13) + two headless probes.
      **Live 2026-08-04**: the count query is cheaper than reading the data (0.04 s vs 0.17 s for
      200 buckets, totals matching exactly); full-list scan 13 days ≈ 2.3 s, 365 days ≈ 40 s;
      resolution matters (251 points flagged at 13 days vs 201 at 365 — gaps shorter than one
      segment cannot appear, so the summary always states "each segment ≈ …")
- [x] **12c** Centre is a two-card swap: overview ⇄ one point. Detail = back button + the
      zoomable timeline + `ValueChart` + the two scrollable tables. The chart is drawn from the
      SAME samples the tables show, decimated to a min/max envelope per pixel column, so the
      graph and the table can never disagree. Custom-drawn — no charting assembly

---
