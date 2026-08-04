---
description: Phase completion status and next items for v2 development
---

# Roadmap

## Status Legend
- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete

---

## Phases 1-8 - complete (archived)
Moved to [`roadmap-archive.md`](roadmap-archive.md) to keep this file under 200 lines:
P1 service layer, P2 selective sync, P3 read-after-write verify, P4 tests, P5 HistSync-as-master
+ multi-tag backfill, P6 polish/interactivity/transparency, P7 scheduling, P8 UX polish +
revert/undo + bidirectional preview.
Still open there: integration test harness (optional), system tray icon (deferred).

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
- [x] Live write acceptance: verified 2026-07-16 — write → read → journal → revert round-trip
      against TESTSV1PC2 in BOTH DST seasons (CET +1 and CEST +2), 22/22 checks, incl. delete
      precision (revert one sample, the +10 s neighbour survives). Closes the revert path, which
      had never been live-verified.

---

## Phase 11 — UTC/local frame fix (2026-07-16)
Root-caused after ~1 h of Secondary data was destroyed by an external helper tool (NOT the app —
its deletes are journal-driven; see `known-issues.md`). The audit then exposed a real bug class.

- [x] **Proven live**: the ClientAccess API frame is **UTC** (returned `Kind=Utc`; a live tag's
      newest sample equals `UtcNow`, not `Now`), and the **`DateTimeKind` of a query start** shifts
      the query by the UTC offset (Local/Unspecified: −59.2 min in Feb, −110.8 min in May)
- [x] **`HistorianDataService.ToApi()/FromApi()`** — one boundary; the API frame stops at the
      service and the rest of the app works in LOCAL time (query bounds, returned times, writes,
      deletes, ReadRaw, ReadInterpolated). Chunk loop stays UTC; `cursor = lastTs.AddTicks(1)`
      preserves Kind (never `new DateTime(ticks+1)`, which drops it and re-reads 1–2 h per chunk)
- [x] Fixed **by construction**: dead live-edge guard (Phase 9's fix had been inert in any non-UTC
      timezone), scheduler no-op at ≤2 h windows, empty "Last 1h" preset, the 1–2 h window shift
      (first hour silently dropped), phantom trailing gray band in coverage
- [x] Reads `ThrowOnItemErrors` (an errored read must never look like "no data"); verified live an
      empty window and a nonexistent tag both return `ItemErrors=0`. `LiveEdgeGraceSeconds` now `> 0`
- [x] Journal frame deliberately unchanged (UTC ticks) → legacy + new entries revert identically,
      no migration. **Do not convert journal ticks to local** — old reverts would delete real data
- [x] Verified live: app now returns `00:00:50=50.8, 00:01:00=51.1 …` **identical to Historian
      Administrator's Trend**; previously `00:00:40=51.3` (raw UTC)
- [ ] Run the MSTest suite in Visual Studio (can't run headless here — MSTest ref unresolvable)
- [ ] Revert read-back: report *confirmed* not *requested* deletions (`MainForm.cs:1693`) — fails safe

---

## Phase 12 — Customer-friendly UI (2026-08-04)
Boss review: customers don't understand *samples, batches, tags, backfill, gap rule*. Goal is a
UI a technician or a manager understands, with the technical surface one switch away.
16 decisions captured with the user before implementation (see the plan file).

### Phase 12a — plain language, EN/DE, Advanced switch, demo mode ✅
- [x] **`UI/Loc.cs`** — every user-visible string, EN + DE side by side, switchable at runtime
      (`Loc.T`/`Loc.F`). No satellite assemblies: delivery stays a single .exe.
      `MainForm.Designer.ApplyTexts()` is the ONE place texts are assigned
- [x] **Plain-language pass**: tag→measurement point, sample→reading, backfill→restore,
      gap→missing period, batch→hidden, coverage→complete, quality %→OK/uncertain/bad.
      Main form + all six dialogs
- [x] **Header strip**: app name · "HOST — main server ↔ HOST — mirror" (`ServerNaming`) ·
      EN|DE · **Advanced** switch. `Settings.AdvancedMode`, default OFF
- [x] **Simple vs Advanced**: Advanced ADDS the filter box, Server statistics, the point link +
      mirror selector, the activity log, Compare/Link-scrolling, the per-direction copy buttons,
      batch counters and the gap rule on the timeline. Simple view = one guarded
      **Restore missing data…** (same preview→confirm flow) + undo history
- [x] **Auto-connect on startup** (`Settings.AutoConnectOnStartup`), then load points and show
      the first one. ⚠ this made the scheduler's "run on startup" reachable for the first time —
      now confirmed once explicitly (`ScheduleStartupConfirmed`), declining switches it off
- [x] **Offline demo mode** (`--demo`): `DemoDataService : HistorianDataService` overrides every
      API method and never calls base, so a demo session cannot reach a server. ~78 generated
      points, seeded outages/independent collectors/point-missing-on-mirror; writes and undos are
      applied in memory. Amber "DEMO DATA" banner. Used for all screenshot verification
- [x] Fixed: blue "restored by this tool" band was drawn 1–2 h off (journal ticks are UTC, the
      axis is local); hidden ComboBox reported an empty point name (analysis could silently fall
      back to the HistSync tag); table caption too long to render
- [x] Verified by screenshot: simple/EN, simple/DE, advanced/EN — all against `--demo`

### Phase 12b — all-points overview ✅
- [x] **`HistorianDataService.ReadBucketCounts`** — `CalculatedQuery(CalculationModeType.Count)`
      returns per-bucket raw sample counts for MANY points in one round-trip. Buckets are filled
      by returned timestamp (not index), pages are drained, times cross via `ToApi`
- [x] **`Services/CoverageScanner`** — chunks of 20 points × both servers, cancellable, with a
      **10 s budget**; what it does not reach stays "not checked yet" plus a *Check the rest*
      button. Never silently truncated
- [x] **`UI/Controls/TagOverviewList`** — one owner-drawn virtualised list (not 78 child
      controls): per row the point name, both servers' bars (green/red/grey exactly like the
      timeline), coverage %, and "~N readings to restore". Worst first; client-side search
- [x] Landing screen: connect → browse → scan → list. The right panel shows the scan's
      estimated totals; "Check for missing data" re-scans the list (or re-checks the open point)
- [x] Estimates are marked as such everywhere. A bucket counts as "has data" from one reading,
      and one-sided buckets are an OUTAGE-level estimate — measured in demo: overview ~3,038 vs
      the point's exact 3,421. Opening a point recomputes with `SyncPlanner`, which stays the
      only thing that decides what a restore writes
- [ ] ⚠ **Live probe still owed**: `CalculatedQuery`/`Count` is proven only against the demo
      service. Before trusting it on plant data, verify counts against a raw read for a known
      window and measure the wall clock for ~78 points × 1 year

### Phase 12c — per-point drill-down ✅
- [x] Centre is a two-card swap: overview ⇄ detail. Detail = "‹ All measurement points" +
      point name, the existing zoomable timeline, the value chart, and the two scrollable
      tables exactly as before (kept visible in both view modes — explicitly requested)
- [x] **`UI/Controls/ValueChart`** — both servers overlaid (main solid, mirror dashed on top so
      near-identical curves stay distinguishable), missing periods shaded red per server, hover
      crosshair with both values. Drawn from the SAME samples the tables show, decimated to a
      min/max envelope per pixel column, and spanning the full width so its time axis lines up
      with the timeline above it. Custom-drawn — no charting assembly
- [x] Verified by screenshot against `--demo`: overview EN + DE, drill-down with the chart,
      Back, and language switching on both cards

---

## Bugs Fixed vs v1
Tracking table moved to [`known-issues-archive.md`](known-issues-archive.md)
(kept growing past this file's 200-line budget).
