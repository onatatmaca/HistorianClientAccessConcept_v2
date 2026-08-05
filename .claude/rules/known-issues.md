---
description: Bugs fixed in v2 (phase 8+) and open items. Older resolved items in known-issues-archive.md, v1 history in known-issues-v1.md.
---

# Known Issues — v2

Current v2 bugs/design defects and their resolutions. Older resolved items are in
[`known-issues-archive.md`](known-issues-archive.md), the UTC incident in
[`known-issues-utc.md`](known-issues-utc.md), v1 pitfalls in
[`known-issues-v1.md`](known-issues-v1.md).

## CALIBRATION: the overview estimate, measured against the planner (Phase 12b, 2026-08-04)
**Location:** `Services/CoverageScanner` · `PointCoverage.EstMissingOn*`

The first version counted every one-sided segment as missing data. Measured live against what
`SyncPlanner` would actually copy (TESTSV1/PC2, 7 days, 576 segments of 17m28s), that was
**fabricating alarms** — and on the dense point it also missed everything real:

| point | readings M/S | coverage | naive | now | SyncPlanner (truth) |
|---|---|---|---|---|---|
| TEMP_04_F02_SCALE | 168 / 172 | 29 % / 29 % | 228 | **0** | 0 |
| TEMP_01_GRS01_SCALE | 168 / 174 | 29 % / 30 % | 202 | **0** | 0 |
| NIVEAU_02_F01_SCALE | 168 / 173 | 29 % / 29 % | 145 | **0** | 0 |
| GASDRUCK_01_F01_SCALE | 1 081 / 1 152 | 46 % / 44 % | 266 | **0** | 0 |
| GASDRUCK_01_GAA_SCALE | 14 115 / 14 536 | 92 % / 96 % | 426 | **1 817** | 2 915 |

Cause of the false alarms: those points log roughly hourly on each server independently, so
each reading lands in a *different* 17-minute segment and nearly every segment looks one-sided.
Cause of the miss: the dense point's gaps are isolated readings *inside* populated segments,
which segment counts cannot see at all.

The rule now has two parts, both computed from the counts already fetched (no extra reads):
1. **Outage runs** — a run of one-sided segments counts only when it is longer than the lacking
   server's OWN typical spacing (`3 × segments/filledSegments`), so cadence jitter cannot
   produce one.
2. **Shared-segment shortfall** — where BOTH servers are recording, the count difference is real
   missing data. Applied only when both fill ≥ 80 % of segments AND their totals are within 25 %
   of each other; otherwise the two are simply recording at different rates and per-segment
   counts mean nothing.

It remains a **lower bound** (1 817 of a true 2 915) and is labelled as such on screen. The
drill-down recomputes with `SyncPlanner`, which stays the only thing that decides what a
restore writes.

## BUG: "0 % complete" painted solid green, and every list row looked identical (fixed 12e)
**Location:** `PointCoverage` · `GapTimeline.DrawTrack` · `TagOverviewList.DrawBar`

Two symptoms, one cause. Completeness was **"fraction of segments holding ≥ 1 reading"** and the
tracks were painted **green, with gap rectangles over the top**. Both are all-or-nothing per
segment, and a segment is hours wide on a long window.

Measured live (`tools/probes/probe-resolution.ps1`, `STAT6.TEMPRL_01_BHKW02_SCALE.F_CV`,
2024-08-05→2025-08-05, 600 segments of **14.6 h**):

| measure | main | mirror |
|---|---|---|
| segments touched (what was drawn) | **100.0 %** | **100.0 %** |
| share of everything recorded (now) | **99.5 %** | **98.8 %** |

and `SyncPlanner`'s 21,261 copyable readings touched **539 of 600** segments — so the same track
was **labelled 100 % and painted 89.8 % red**. Every row in the all-points list was solid green
at ~100 % for the same reason. A server with NO readings produced no gap rectangles at all, so
it kept the green base: **0 % and green**.

**Fix:** one definition in both cards — per segment, the share of the readings the better-served
server has (`PointCoverage.SegmentShare` / `ShareOfBest`), and the bars and tracks are painted
green *in proportion* to it. The percentage and the picture are then the same quantity by
construction. The yardstick is the other server, not an absolute rate, because this tool can
only ever make one server match the other — and that also survives deadband tags, since both
servers see the same deadband. `SegmentsTouched` survives only as the density gate inside the
estimate, which was calibrated against it.

Note the remaining honest difference: red area is "share of readings the other server has that
this one lacks", while `SyncPlanner` decides what a restore actually WRITES (in
independent-collector mode it fills only real outages). The planner remains the only thing that
drives a write, and its exact number is what the right panel and the missing-data table show.

## PERF: 1,006 MB for one point over a year, in an x86 process (fixed 12e)
**Location:** `Forms/MainForm.cs` · `GridRow` · `UI/Controls/ValueChart.cs`

Measured live opening one point on a 1-year window. x86 runs out of address space around
1.2 GB, so this was close to failing, not merely slow. Two causes:
1. Every table row cached its three DISPLAY STRINGS (~150 bytes/row) although the grids are
   **virtual** — millions of strings built only to be discarded. `GridRow` now holds the raw
   `DateTime`/`float`/quality (~32 bytes) and `CellValueNeeded` formats the cells actually on
   screen. **1,006 MB → 639 MB**, and the tables appear immediately.
2. `ValueChart` rebuilt its decimated min/max envelope **inside `OnPaint`** — ~3 M readings per
   server per repaint, and the hover crosshair repaints. Now cached per (data, width, plot,
   range). Same picture, same detail.

**Still open:** the remaining 639 MB is the gap analysis re-reading both servers into its own
`List<DateTime>` for a window `ReadPrimaryData`/`ReadSecondaryData` just loaded. Reusing those
would remove a whole second read per server. Deferred to the audit — it changes what the
analysis sees.

## BUG: a demo session displayed the real server addresses (fixed 12e)
`LoadSettings` loaded the persisted hostnames and the demo block overwrote them *afterwards* —
but the header strip and the timeline track labels are derived in between. A `--demo` screenshot
showed `192.168.50.186 — main server`. The names are set inside `LoadSettings` now. Demo mode
exists so a screenshot can never be mistaken for live data; that guarantee is the whole point.

## BUG: a one-sided point was offered a restore it could never deliver (fixed Phase 12d)
**Location:** `Forms/MainForm.cs` · `RunGapAnalysis`, `BuildTrack`, `UpdateGapAnalysisUI`

Opening a point that exists on only ONE server showed the other server at 0 % and the right
panel offered to restore every reading into it (19,086 in the demo). The all-points list
already **excluded** one-sided points from its totals — "the tool writes readings, it does
not create measurement points" — so the two cards gave different answers for the same point.
On the live rig that is not a corner case: **201 of 273 points are one-sided.**

Root cause: `hasPrimary`/`hasSecondary` only asked whether the server was CONNECTED, so a
server without the point looked like a server holding nothing. `GapAnalysisService` emits one
whole-period gap for an empty server and `SyncPlanner` then sees "target empty, source full"
→ copy everything.

**Fix:** `hasPrimary &= _pointOnMain`, `hasSecondary &= _pointOnMirror` — a server that lacks
the point is not read, not planned and not counted; the track, the empty table and the
summary all say "not set up on this server". Presence is trusted only for names that came
from a browse (`SetPointPresence`), so a hand-typed name is never hidden behind a false
"not set up" — that failure would be worse than the number it replaced.

**Measured on the Historian, not on the app** (2026-08-05, `probe-onesided.ps1`): an exact
browse for `STAT6.CH4_01_H2S.F_CV` returns **0 hits on main, 1 on the mirror**, and a read on
main throws **`InvalidTagname`** — the restore was undeliverable, not merely pointless. Of 47
one-sided points sampled, 0 returned data on the other server. The write path was never at
risk: it offers only the intersection of both servers (`TryGetSharedTags`).

## BUG: button captions clipped mid-word, silently (fixed Phase 12d)
**Location:** `UI/Controls/FlatButton.cs`

A plain WinForms `Button` clips overflowing text with no ellipsis, so in Advanced/German
"← Auf Hauptserver kopieren" rendered as "← Auf Hauptserver" and "Vorschau &&
wiederherstellen…" as "Vorschau &" — the same silent-truncation class as the Phase 12a
caption bug. `FlatButton` now measures the painted text (`&&` → `&`) and shrinks the font to
fit down to 7.25 pt, with `AutoEllipsis` as the last resort so overflow is at least VISIBLE.
Advanced also gives the action column 196 px instead of 156 and shortens the chart, because
its second button group plus the activity log did not fit at all and the bottom-docked repair
group was losing its header off the top.

## BUG: "restored by this tool" band drawn 1–2 h off (fixed Phase 12a)
**Location:** `Forms/MainForm.cs` · `LoadBackfilledRanges`

Journal ticks are stored in **UTC** on purpose (so legacy and new entries revert identically),
but the band was read back with a plain `new DateTime(tk)` — `Kind=Unspecified` — and then
compared against the LOCAL analysis window and drawn on a LOCAL axis. The blue band therefore
sat 1 h (CET) or 2 h (CEST) away from the data it described, and was clipped against the wrong
window edges. **Fix:** `new DateTime(tk, DateTimeKind.Utc).ToLocalTime()`, exactly what
`RevertBackfill` already does. Display-only — the revert path itself was always correct.

## BUG: a hidden ComboBox reports NO selected point (fixed Phase 12a)
**Location:** `Forms/MainForm.cs` · point selection

The simple view hides the mirror point selector. A WinForms `ComboBox` with
`DropDownStyle.DropDown` keeps its text in the edit control, so while it is hidden (no window
handle) **`ComboBox.Text` returns "" and `SelectedItem` returns null** even though a point is
bound and selected. That is not cosmetic: `RunGapAnalysis` treats an empty point name as
"use the configured HistSync tag", so the app would have analysed — and offered to repair — a
different point than the one on screen.

**Fix:** the selected point is explicit app state (`_pointPrimary` / `_pointSecondary`), set
whenever the selection changes and used by every consumer; the combos are only an input device.
`PointName(combo)` is the one place that reads a combo, and it prefers typed text only while the
control really has a handle. **Rule: never derive state from a control that the view mode may
hide.**

## BUG: table caption silently truncated (fixed Phase 12a)
`"HOST — point"` did not fit the caption label above each data table and rendered as `"HOST — "`
with the point name simply gone — no ellipsis, no clue. Captions are now the point name alone
(the button above already names the server, the header strip names both hosts) and both labels
have `AutoEllipsis = true` so overflow is visible instead of silent. Related: setting `.Text`
while a modal progress dialog covers the window does not always reach the screen — the caption
is now written in one place (`UpdateGridHeaders`) which ends with `Refresh()`.

---

## INCIDENT: UTC frame vs `DateTime.Now` — moved to [`known-issues-utc.md`](known-issues-utc.md)
The 2026-07-16 data-loss incident (external tooling, not the app), the proof that the API frame
is UTC, and the single-boundary fix. **Still open there:** revert reports *requested* not
*confirmed* deletions (no read-back) — fails safe, but the message is optimistic.

## Phase 10 bugs — moved to [`known-issues-archive.md`](known-issues-archive.md)
Resolved and stable: the median-based gap rule that collapsed coverage on deadband tags, the
exact-second diff that reported phantom copies on independently-collecting redundant servers
(→ `SyncPlanner`), and `ReadRawInRange` paging to the end of the archive plus the server
cursor-leak trap. **Never abandon a paged RawByTime query** — that one applies to any script
touching the API too.

## DOC FIX: IData.Add's second argument is errorOnReplace, not "allowOutOfOrder"
Verified by reflecting the v1.6.1.0 DLL: `Add(DataSet dataset, Boolean errorOnReplace,
ItemErrors& errors)`. Passing `false` (as we always did) means "silently replace an
existing sample at the same timestamp" — which is what backfill wants. The old
`historian-api.md` claim that it was an out-of-order flag was wrong; out-of-order
historical writes need no flag at all.

---

## NOTE: WinForms UIA quirks (dev tooling only)
Driving the UI via UI Automation from scripts proved flaky for element enumeration on
this form (buttons intermittently invisible to `FindAll`). The screenshot/verification
scripts therefore click by coordinates. Not a product issue — end users don't use UIA.
