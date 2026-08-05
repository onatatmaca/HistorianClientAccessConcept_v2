---
description: Phase completion status and next items for v2 development
---

# Roadmap

## Status Legend
- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete

---

## Phases 1–12c — complete (archived)
All in [`roadmap-archive.md`](roadmap-archive.md), which keeps this file under 200 lines:
P1 service layer · P2 selective sync · P3 read-after-write verify · P4 tests · P5
HistSync-as-master + multi-tag backfill · P6 polish/interactivity/transparency · P7 scheduling ·
P8 UX polish + revert/undo + bidirectional preview · **P9** sync timeline, click-to-zoom, one
modal progress surface, live-edge guard, IP+port · **P10** cadence-aware calculation
(`SyncPlanner`, p90 gap rule, bounded `ReadRawInRange`) · **P11** the UTC/local frame fix ·
**P12a** plain language + EN/DE + Advanced switch + demo mode · **P12b** all-points overview ·
**P12c** per-point drill-down + value chart.

Still open there: integration test harness (optional), system tray icon (deferred), the
MSTest suite needs Visual Studio to run, and one probe re-run noted under P12b.

---

## Phase 12d — second UI review (2026-08-05) ✅
Boss review of the live build. Two defects first — both make the app show WRONG numbers.

### Defects
- [x] **B1 — opening a point that exists on one server only showed the WRONG point's data.**
      Reproduced: `STAT6.V_EIN_02_MB02.F_CV` (mirror-only) opens with the main table full of
      readings from the previously selected point, under a caption naming the new one. Root
      cause to confirm: `SyncCombo` silently does nothing when the point is not in that
      server's list, so `_pointPrimary/_pointSecondary` and the combo disagree, and the async
      `SelectedIndexChanged` can then overwrite the explicit selection.
- [x] **B2 — the same point reported two different completeness figures.** List says 74.5 % /
      77.3 %, opening it says 32.9 % / 33.9 %. They are two different definitions (segment-fill
      vs the p90 gap rule) wearing the same label and the same colours. Must be reconciled or
      renamed — verify against raw data first, then decide.

### UI
- [x] **U1** Value chart: two stacked charts (main above, mirror below) like the completeness
      bars, taller, no overlapping axis labels/legend
- [x] **U2** "Enlarge" button on the chart → large closable window (`Forms/ChartDialog`)
- [x] **U3** Remove the "Load data — main/mirror" buttons; loading is automatic
- [x] **U4** Link scrolling + Compare available in the simple view too
- [x] **U5** An empty table names WHICH of four things is true: no point chosen · point not
      set up on that server · point present but no readings in this period · **the read
      failed** (an errored read must never pass for "the server holds nothing")
- [x] **U6** Server fields are `ComboBox`es backed by `Settings.ServerHistory` (semicolon-
      separated, most-recent-first, cap 10). Only addresses that actually CONNECTED are
      remembered — offering a typo back would make it look like a valid choice
- [x] **U7** "‹ All measurement points" made prominent (button, not a faint link)
- [x] **U8** Point-name captions were clipping a 33-char name to 32 with no ellipsis —
      that is why ".F_CV" read as ".F_C" in the screenshots. Non-bold + AutoEllipsis

### Found while verifying the above (both worse than the list itself)
- [x] **A point that exists on only ONE server offered to restore into the server that does
      not have it** — 19,086 readings in the demo, and the live rig is 201 one-sided points
      of 273, so this was the normal case there. The all-points list already excluded these
      from its totals, so the two cards contradicted each other about the same point: the
      exact defect B2 was about. A server without the point is now treated as *un-analysable
      for it* (`hasPrimary/hasSecondary &= _pointOnMain/_pointOnMirror`) — no read, no plan,
      no count; track, table and summary all say "not set up on this server". Presence is
      only trusted for names that came from a browse (`SetPointPresence`), so a hand-typed
      name can never be hidden behind a false "not set up". The write path was never at risk
      (it only ever offers the intersection). **Proven on the Historian**: an exact browse for
      `STAT6.CH4_01_H2S.F_CV` returns 0 hits on main, 1 on the mirror, and a read on main
      throws `InvalidTagname` — a restore there was undeliverable, not merely pointless
- [x] **Advanced mode clipped its own action column**: the REPAIR header was pushed off the
      top of the bottom-docked group and the German captions were cut mid-word ("Auf
      Hauptserver" for "Auf Hauptserver kopieren"). `FlatButton` now shrinks a caption to fit
      (down to 7.25pt) and only then ellipsises, so a caption cannot silently go missing
      again; Advanced also trades chart height (214→118) and grid width (156→196) back
- [x] The enlarged chart draws its own **time axis** — on the main screen the completeness
      timeline directly above carries the dates, but the dialog has no such reference

### Verified live (2026-08-05, 192.168.50.186/.187, READ-ONLY)
`scratchpad/probe-onesided.ps1` + `probe-crosscheck.ps1`, against the Historian, not the app:
- Browse counts match the app exactly: main 79 · mirror 266 · shared 72 · main-only 7 ·
  mirror-only 194 · union **273**, and the app's "201 not on both servers" = 7 + 194
- 47 one-sided points sampled: **0** returned data on the other server (the presence test
  never hides a real point)
- `STAT6.BHKW_01_GAS.F_CV`, 2026-05-20→27: raw **10,080 / 10,078**; `SyncPlanner` says
  **5 → mirror, 3 → main** and segment completeness **100.0 % / 100.0 %** — every one of
  those is exactly what the app printed, and 5 − 3 = the raw net of 2

## Phase 12e — what "complete" means, and x86 memory (2026-08-05) ✅
Raised by the user from live screenshots: a track reading **0 % painted solid green**, and every
row of the all-points list looking identical while the same points, opened, were almost entirely
red. Both were the same defect — see the measured table in
[`known-issues.md`](known-issues.md).
- [x] One definition of completeness in both cards: the share of everything recorded for that
      point that this server holds, with bars and tracks painted green **in proportion** to it,
      so the number and the picture are the same quantity by construction
- [x] **"Check the rest" removed** — a scan checks every point, however long it takes (273
      points over a full year: **20 s**). A partial answer to "what is missing?" is worse than
      a slower complete one
- [x] `ValueChart` caches its decimated envelope instead of rebuilding it inside `OnPaint`
      (~3 M readings per server, on every hover)
- [x] `GridRow` holds raw values, not display strings — **1,006 MB → 639 MB** on the same point
- [x] A `--demo` session no longer shows the real server addresses
- [x] `PointCoverageTests` updated, including the saturation case the change exists for

## Phase 13 — full audit before shipping

### Carried in from 12e, with measurements
- [ ] **Gap analysis re-reads both servers** for a window the tables just loaded (the remaining
      639 MB, and a doubled read time per point). Reuse `_rawPrimarySamples` when the tag and
      window match — verify it does not change what the analysis sees
- [ ] **`SyncPlanner`'s 90 % aligned-stream threshold is borderline on real pairs.** Measured on
      `STAT6.TEMPRL_01_BHKW02_SCALE.F_CV` over a year: exact-second match **90.6 %**, so it took
      the exact-diff branch and reported **21,261** readings to copy, while share-of-best puts the
      real shortfall near **1 %**. Just under the threshold it would have reported only genuine
      outages. **Do not touch the threshold without a lot more measurement** — it decides what
      gets WRITTEN
- [ ] Compare mode still materialises one row object per sample on both sides; on a year-long
      window that is the same scale problem the tables just shed

- [x] Parallel code audit (subagents): write/delete safety, UTC boundary, async + cancellation,
      UI state machine, error handling, resource lifetime, dead code. **Findings register with
      the measurements behind each: [`audit-phase13.md`](audit-phase13.md)** — 8 open HIGH items,
      5 MEDIUM, 4 fixed, one page of verified dead code
- [x] **The test suite runs headlessly at last** — `tools\run-tests.ps1`, 118 tests in ~0.2 s, no
      Visual Studio. It had never been in a verification loop, and it was hiding a red test
      (`GapAnalysisService` left `TotalSamples=0` on the single-sample path). Fixed; 118/118
- [~] Live cross-check: `probe-crosscheck` re-confirmed Phase 12d exactly (10,080/10,078 raw,
      5 → mirror, 3 → main) and a new `probe-memory` measured the per-point cost. **But the
      cross-check probe still computes the pre-12e "segments touched" completeness** — it would
      confirm a number the app no longer claims. Update it, then re-run against the detail card
- [ ] Fill test gaps found; all probes green

### All eight HIGH findings fixed (2026-08-05) ✅ — measurements in `audit-phase13.md`
- [x] **The planner was proposing ~72 k phantom writes.** Swept all 72 shared points over 30 days:
      the match rate cannot separate aligned pairs from independent collectors (14 offenders sat
      at 90.8–97.5 %). The signal is SYMMETRY — both servers holding a similar share the other
      lacks, which no aligned pair does. Live, the populations are **250x apart** (0.01 % vs
      2.54–9.18 %), so aligned now also requires `OneSidedShare <= 1 %`. Re-measured:
      **146,670 → 74,502** planned writes, exact-diff points **17 → 3**, both-direction offenders
      **14 → 0**, and the healthy pair still finds its real 5 → mirror / 3 → main
- [x] **Failed reads are no longer presented as empty servers** — `SafeReadTimes` returns null
      (which callers already treat as "not read"), and neither preview dialog substitutes an
      empty list or claims "in sync" over points it could not compare
- [x] **A restore that cannot be journaled now says so** instead of reporting success on data it
      can never undo
- [x] **One operation at a time** — `btnRestore` is disabled during runs, every write entry point
      is gated, and the gate covers the WHOLE unattended run, so a manual restore can no longer
      dispose the scheduled run's cancellation token
- [x] **DST-ambiguous keys** — diff/verify keys are UTC, so the repeated autumn hour cannot make
      two instants share an identity; journal ticks on disk unchanged (pinned by a test)
- [x] **A total one-server outage** is counted and sorted first instead of painted green
- [x] **The scheduler cannot silently widen its own scope** to the `*` mask

## Phase 14 — v1.0 for office testing
- [ ] Version 1.0.0, installer, single-folder deployment
- [ ] User documentation ≤10 pages, EN + DE, with screenshots: install, configure, use

---

## Bugs Fixed vs v1
Tracking table moved to [`known-issues-archive.md`](known-issues-archive.md)
(kept growing past this file's 200-line budget).
