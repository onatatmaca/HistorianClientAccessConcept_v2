---
description: Bugs fixed in v2 (phase 8+) and open items. Older resolved items in known-issues-archive.md, v1 history in known-issues-v1.md.
---

# Known Issues — v2

Current v2 bugs/design defects and their resolutions. Phases 5–6 (resolved & stable)
moved to [`known-issues-archive.md`](known-issues-archive.md); v1 pitfalls are in
[`known-issues-v1.md`](known-issues-v1.md).

---

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

## INCIDENT + OPEN DEFECTS: UTC frame vs `DateTime.Now` (2026-07-16, audited)

**Proven** (live probes, see [`historian-api.md`](historian-api.md)): the API frame is **UTC**
(returned `Kind=Utc`), and a `Local`/`Unspecified` **query start** is converted local→UTC, so the
query **starts early by the UTC offset** (1 h winter / 2 h summer) and returns samples before `from`.

### Data loss (external tooling, NOT the app) — fixed + data restored
A throwaway helper read `[from,to]` with only `if (ts > to) break;` (**no `ts < from` guard**) and
deleted every timestamp it read ⇒ **~1 h of Secondary data BEFORE the window was destroyed** (API
hour `2026-02-09 23` = Historian-local `2026-02-10 00:00–01:00`); backfills, correctly scoped by
`SyncPlanner`'s `t >= evalFrom`, never restored it. Restored from Primary (2,343 samples).
**The app cannot do this**: its only `IData.Delete` consumes **journal ticks**, never a read
(`MainForm.cs:1674`), and `SampleFilter.cs:35` has the `if (s.Time < start) continue;` guard.
`IData.Delete` has no range overload (verified by reflection).

### FIXED — the app now converts at the API boundary (single point)
`HistorianDataService` gained `ToApi()` / `FromApi()`; **the API frame stops at that service and the
rest of the app works in LOCAL time**. Applied to every crossing: query bounds, returned times,
`WriteFloatSamplesWithQuality`, `DeleteSamples`, `ReadRaw`, `ReadInterpolated`. `ReadRawInRange`
runs its chunk loop in UTC (`cursor = lastTs.AddTicks(1)` **preserves Kind** — never
`new DateTime(ticks+1)`), clips in UTC, then hands back local.

**Verified live** (2026-07-16, picker-style `Kind=Local` window `2026-02-10 00:00→00:03`): the app
now returns `00:00:50=50.8, 00:01:00=51.1, 00:01:10=51.4 …` — **identical to Historian Trend**,
`Kind=Local`. Previously it returned `00:00:40=51.3` (the UTC frame).

This one change fixed **by construction**: the dead live-edge guard, the scheduler's future-window
no-op, the empty "Last 1h" preset, the ~1–2 h window shift (first hour silently dropped: 8,383 read
→ 8,167 kept), and `GapAnalysisService`'s phantom trailing gray band — all were `DateTime.Now`
compared against UTC sample times; both sides are now local.

Also fixed: reads now `ThrowOnItemErrors` (an errored read must never look like "no data" — that
would force exact-diff on an empty target, mass-copy, journal it, and a later revert would delete
pre-existing data). Verified live that an empty window **and** a nonexistent tag both return
`ItemErrors=0`, so it cannot fire on the normal empty case. `LiveEdgeGraceSeconds` now requires
`> 0` (a 0 grace disabled the write/collector race guard).

**Journal frame — deliberately unchanged (this was the landmine):** journals on disk hold **UTC**
ticks. Journaling now converts `bs.Time.ToUniversalTime()` and revert re-tags
`new DateTime(tk, DateTimeKind.Utc)`, so **legacy and new entries revert identically with no
migration**. Do NOT "simplify" this to local ticks — old journals would then delete at an instant
1–2 h off, i.e. real plant data.

**Never affected:** copied values always landed at the correct absolute instant (same frame both
sides), and revert always deleted the correct instant (journal stores `long[] Ticks`, never
`DateTime`, so the serializer never converted them). No data was ever corrupted by this class.

**Still open:** revert reports *requested* not *confirmed* deletions (`MainForm.cs:1693`, no
read-back) — fails safe (deletes nothing, but the message would be optimistic).

---

## BUG: Coverage collapsed on deadband tags — median-based gap rule (fixed Phase 10)
**Location:** `GapAnalysisService.Analyze`

Real plant tag TEMP_02_WS logs every ~6 min (median) but normally stays quiet 30–60 min
(deadband). `threshold = max(median×1.5, 120s)` ≈ 9.5 min flagged every normal quiet
period → 41% coverage shown for a healthy tag. **Fix:** per-tag rule =
`max(p90(intervals) × GapThresholdMultiplier, MinimumGapSeconds)` — if ≥10% of a tag's
intervals reach a duration, that duration is its cadence. The `Percentile` index is
capped below the max so a lone real outage in a sparse window can't masquerade as
cadence. Rule shown on each timeline track ("gap rule: silence > 1h 0m"). Verified
live: TEMP_02_WS now 100%/100% on the exact window that showed 41%.

## BUG: Exact-second diff = phantom copies on redundant collectors (fixed Phase 10)
**Location:** everywhere the whole-second diff ran → new `Services/SyncPlanner`

The two plant servers collect INDEPENDENTLY — same values logged seconds apart
(offsets 5–120s). The exact-second diff reported them all as "missing": measured live,
GASDRUCK_01_GAA showed 33,376 of 47,474 samples "missing" when ~98 genuinely were;
TEMP_02_WS showed 707 where 599 had an identical-value partner within 30s. A backfill
would have permanently interleaved both collectors' streams (double density, both
directions, every tag). **Fix:** `SyncPlanner.Plan` auto-detects per tag:
exact-second match rate ≥90% ⇒ aligned streams (same-source data, e.g. HistSync or
tool-written) → keep exact diff (catches isolated misses); otherwise → copy ONLY
source samples inside real TARGET OUTAGES (silence > the tag's own gap rule).
ExecuteBackfill, both preview dialogs, the amber strip and the missing-data table all
use the same planner — every surface shows what a backfill would actually write.

## BUG: ReadRawInRange paged to the archive end + server cursor-leak trap (fixed Phase 10)
**Location:** `HistorianDataService.ReadRawInRange`

Despite the Phase 9 claim, it still ran `RawByTimeQuery` to the END of the archive and
clipped client-side (a 13-day window on the 2-year Genthin archive read ~50× too much).
The naive fix — breaking out of the pagination early — LEAKS a server-side cursor per
abandoned query until expiry (verified live: "Maximum number of cached items exceeded",
which then fails later queries too). **Fix:** bounded `RawByNumberQuery` chunks
(5000/call, each drained), stopping at the range end. Never abandon a paged RawByTime
query — that applies to any script touching the API as well.

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
