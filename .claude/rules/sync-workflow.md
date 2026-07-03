---
description: Gap analysis algorithm, sync timeline, backfill workflow, and batch planning
---

# Sync Workflow

Scheduling + revert/undo live in [`scheduling-and-revert.md`](scheduling-and-revert.md).

## Source Tag Selection (linked tags as of Phase 9)
Gap analysis uses the **currently selected tag** on each side (`cboPrimary` / `cboSecondary`).
If a combo is empty, it falls back to the configured HistSync heartbeat tag (app.config
`SyncTagName`, default `"HistSync"`).

**Tag link (default ON, persisted `TagLinkEnabled`):** picking a tag on one side
auto-selects the identical tag on the other side when it exists there (both directions),
then reads both grids and re-analyzes once. The toggle button between the combos
("⇄ Linked…" / "✕ Not linked…") turns this off for deliberately comparing different
tags. If the tag doesn't exist on the other server, the selection stays un-mirrored and
the status bar says so. Mirroring happens with events suppressed (`_isLinkPropagating` +
`_suppressAutoRead`) so the flow stays one predictable sequence.

Backfill feasibility for a gap is checked against the **same tag** on the opposite
server — you can only backfill what actually exists on the source.

## Gap Detection Algorithm (Phase 10: per-tag p90 rule)
1. Fetch raw sample times via `ReadRawInRange` (Phase 10: bounded `RawByNumberQuery`
   chunks — stops at the range end AND never abandons a pagination cursor; abandoned
   RawByTime cursors leak server-side, "Maximum number of cached items exceeded")
2. `gapThreshold = max(p90(intervals) × GapThresholdMultiplier, MinimumGapSeconds)`
   (app.config: multiplier default 2.0, floor default 120s). p90, NOT median — deadband
   tags legitimately stay quiet far past their median (TEMP_02_WS: median 6 min, normal
   quiet 30–60 min; the old median×1.5 rule showed 41% coverage on a healthy tag). The
   percentile index is capped below the max delta so one true outage in a sparse window
   can't inflate the rule. The rule is SHOWN on each track ("gap rule: silence > 1h").
3. Any delta > threshold becomes a `GapWindow`; leading/trailing vs. the evaluation
   window too; empty server ⇒ one whole-period gap
4. Each window splits into `GapBatch`es; `MarkBackfillFeasibility` sets `CanBackfill`
   per batch from the OTHER server's data (half-open `[start, end)`, binary search)

`coverageRatio = (totalSpan − missingDuration) / totalSpan`. Display-only — backfill
planning is `SyncPlanner` (below).

## SyncPlanner — the ONE definition of "what would a backfill copy" (Phase 10)
`Services/SyncPlanner.Plan(sourceTimes, targetTimes, from, to, floor, multiplier)`
is used by ExecuteBackfill, BOTH preview dialogs, the amber strip and the
missing-data table, so every surface reports identical numbers. Per tag it
auto-selects one of two modes:
- **Aligned streams** (exact-second match rate ≥ 90%, or either side < 20 samples):
  same-source data (HistSync, tool-written) → exact whole-second diff; catches
  isolated missing samples.
- **Independent streams** (redundant collectors on their own clocks — timestamps
  offset 5–120s; measured live: exact diff flagged 33,376 of 47,474 samples on
  GASDRUCK_01_GAA when ~98 were genuinely missing): copy ONLY source samples inside
  real TARGET OUTAGES (silence > the tag's gap rule). Never interleaves the two
  collectors' streams; healthy offset servers report "In sync". Re-runs are
  idempotent (copied seconds exist on the next diff).

## SYNC TIMELINE (Phase 9 — replaces the two CoverageBars)
Full-width `UI/Controls/GapTimeline` at the top of the center panel: both servers on
ONE shared time axis so differences line up visually.

- **Tracks**: Primary above, Secondary below; green = data, centered coverage %.
- **Red** = missing here and the other server HAS it (fillable; from per-batch
  `CanBackfill`, consecutive batches merged via `IntervalBuilder.SplitByFeasibility`).
- **Gray** = missing on BOTH servers → nothing to copy (answers "why is it never
  100 %?"). When the opposite server wasn't read, everything renders red, not gray.
- **Blue bottom band** = backfilled by this tool (non-reverted journal entries for
  this host+tag, clipped to the window; reverting removes the band).
- **Amber strip** between tracks = copy candidates: merged runs of the samples
  `SyncPlanner` would copy — exactly what a backfill writes, nothing phantom. Only
  when both sides show the same tag. In independent-stream mode a note explains it
  ("outage-fill mode — collectors log independently (timestamps match 55%) …").
- Track labels carry the per-tag rule: "gap rule: silence > 1h 0m" — transparency
  for "why is/isn't this red".
- **Axis** with adaptive ticks (minutes → months), hover crosshair + time readout,
  tooltips on every segment, legend row.
- **Click any segment → zoom**: date pickers jump to that span (±10 % padding) and
  analysis re-runs; "⟲ zoom back" in the section header pops the previous range
  (`_zoomStack`).

All analysis + interval preparation runs inside `Task.Run` (real archives hold
millions of timestamps; the UI thread only binds results).

## Missing-data table (right panel)
Columns `Tag | Missing on | Count | Period`, one row per direction per tag, built from
the same fetched samples via `SyncPlanner` (`AddPlanRow`) — counts are what a backfill
would write. Hover = full sentence; **click a row → zoom** the timeline to that
period. Summary label: "Backfill would copy N sample(s) → Secondary …" or "In sync".

## Modal progress dialog (Phase 9 — single Cancel)
Every long operation (connect/browse/read/compare/analyze/backfill/revert) runs behind
one modal `Forms/ProgressDialog` with exactly one Cancel button — the old status-bar
progress bar, its Cancel and the separate Stop button are gone, and the main window
cannot be poked mid-operation.

- `SetBusy(true, title)` starts a 400 ms delay timer; quick ops never flash a dialog.
  The tick shows the dialog via `ShowDialog` (nested pump keeps the operation's
  continuations flowing); `SetBusy(false)` closes it.
- `SetStatus` text mirrors into the dialog; `SetPhaseProgress` (tag x/y) and
  `SetProgress` (batch x/y) drive two bars; elapsed time ticks every second.
  **No ETA by design** — batch sizes vary too much for an honest estimate.
- Cancel (or ESC / Alt+F4) disables the button ("Cancelling…") and cancels `_cts`;
  the operation stops at its next token check.
- Scheduled/headless runs set `_suppressOpDialog` — no dialog ever appears for them.

## Backfill Execution: ExecuteBackfill (SyncPlanner as of Phase 10)

```
evalTo = min(evalTo, now − LiveEdgeGraceSeconds)     // Phase 9 live-edge guard
for each tag:
    srcData = ReadRawInRange(source, tag, evalFrom, evalTo)
    tgtData = ReadRawInRange(target, tag, evalFrom, evalTo)
    plan    = SyncPlanner.Plan(srcTimes, tgtTimes, evalFrom, evalTo, floor, mult)
    missing = srcData whose Time ∈ plan.ToCopy      // aligned→exact diff; else outages
    batches = SampleBucketer.GroupByBucket(missing, BatchSize)
    for each batch:
        WriteFloatSamplesWithQuality(target, tag, batch)   // quality preserved
        verify: re-read ±1s, confirm EACH written second is present
        BatchesSucceeded++ iff writeOk && verifyOk; journal written seconds
```
The activity log names the mode per tag ("aligned streams — exact diff" / "N target
outage(s), gap rule 1h 0m").

**Live-edge guard**: on live servers the collectors are still writing near "now", so
diffing up to now reports in-flight samples as missing on every run — an endless
backfill. Every write path clamps the evaluation end (ExecuteBackfill itself, both
Copy buttons, Preview & Backfill, scheduled runs) so preview counts match what is
written. Analysis display is NOT clamped.

**Cancel mid-run** (via the progress dialog): stops at the current batch, journals
what was written, then asks *keep* (default; revertable later) or *revert now*.

## Preview dialogs (per-tag mini timeline as of Phase 9)
Both dialogs compute per-tag stats on a background thread with the same whole-second
diff as the backfill, and now include a compact `GapTimeline` (no zoom) at the bottom:
**click a tag row → see both servers' coverage for that tag on one axis**, amber =
exactly what would be copied. Interval data is distilled while the stat pass holds the
sample lists (`IntervalBuilder.CoverageIntervals/Complement/Intersect`) — only merged
ranges are kept, never raw samples (x86 memory).

- `TagSelectionDialog` (Copy to X buttons): source→target only; columns
  `✓ | Tag | Source samples | Target samples | Will copy | Write range`; in-sync rows
  dimmed; OK disabled until stats load.
- `BidirectionalBackfillDialog` (Preview & Backfill): two checklists side by side
  (P→S and S→P), tags in sync in a direction aren't listed; runs each chosen
  direction and shows one combined report.

## Post-Backfill Report (`Forms/SyncReportDialog.cs`)
Modal after every attended backfill: source→target header, duration, totals, per-tag
grid (Attempted · Succeeded · Failed · Samples · Errors), rows tinted on failure,
CSV/TXT export. Suppressed for unattended runs (file log instead).

## State Dependencies & auto-analyze
- Backfill needs both servers connected; the dialogs' own diff decides what is copyable
  (no dependency on gap windows having been found).
- After backfill/revert → `AutoRefreshAfterBackfill`: exits compare mode, re-runs
  analysis (timeline + table), re-reads loaded data grids.
- **Date/time edits and the quick-select presets do NOT auto-run** (boss request
  2026-07: a DateTimePicker fires `ValueChanged` per field, so editing day→month→hour
  kicked off a run on every keystroke). A new time range is analyzed only when the user
  clicks **Analyze Gaps** (which also re-reads loaded grids) or via click-to-zoom
  (`SetRangeAndAnalyze` → `RunGapAnalysis` directly).
- **Tag changes still auto-analyze** after a 500 ms debounce (timeline + table only):
  picking a tag is one deliberate action and the data grids already re-read on it, so
  the timeline must follow the selected tag or it would show a different tag than the
  grids. Suppressed while busy.
