---
description: Phase 13 audit findings register - what was verified, what is open, and the measurements behind each
---

# Phase 13 audit — findings register (2026-08-05)

Seven dimensions, four parallel auditors, every claim re-verified against the code or the live
rig before it was written here. **Agent claims that did not survive verification are recorded as
wrong** — that is the point of this file.

## Measured first (live, READ-ONLY, `tools/probes/probe-memory.ps1`)

`STAT6.TEMPRL_01_BHKW02_SCALE.F_CV`, 2024-08-05 → 2025-08-05, TESTSV1/PC2:

| step | managed | private |
|---|---|---|
| baseline | 4.2 MB | 51.8 MB |
| both servers read (225,473 + 223,913 readings) | 16.0 MB | 82.5 MB |
| + one row object per reading, both servers | 29.7 MB | 89.0 MB |
| + the analysis re-reading the same window | 42.4 MB | 110.3 MB |
| + `SyncPlanner` both directions | 52.5 MB | 141.0 MB |

**A year of this point is 225 k readings, not the ~3.15 M an auditor assumed** — every memory
figure it derived was ~14x too large ("380 MB of row objects", "330 MB transient"). The real
costs: row-object duplication **13.7 MB**, the duplicate read **12.7 MB**, the planner **10.1 MB**.
Real, worth fixing, and nowhere near the x86 ceiling. Do not re-open these as emergencies.

## CRITICAL / HIGH — all eight FIXED (2026-08-05), measured below

**1. `SyncPlanner` takes the exact-diff branch on two independent collectors, and would write
~41 k phantom readings.** Measured above: match rate **90.6 %**, just over the 90 % threshold, so
`UsedExactDiff=True` in BOTH directions and the plan is **21,306 → mirror AND 19,746 → main**.
Two servers cannot each be missing ~20 k readings the other holds; that symmetry IS the signature
of independent streams. A both-directions restore here permanently interleaves both collectors'
streams — the exact catastrophe `SyncPlanner` was built in Phase 10 to prevent.
*A large ToCopy in both directions at once is a stronger independence signal than the match rate.*
**Do not change the threshold without re-measuring across many tags** (roadmap already says so);
this entry adds the measurement and a candidate signal.

**2. Both preview dialogs turn a failed TARGET read into "the target holds nothing", then offer
to copy everything.** `Forms/TagSelectionDialog.cs:304` `catch { tgtSamples = null; }` and
`Forms/BidirectionalBackfillDialog.cs:299` `catch { secData = new List<...>(); }`. `SyncPlanner`
then sees `tgt.Count < 20` → exact diff → `ToCopy` = every source sample. `HistorianDataService`
throws on `ItemErrors` *specifically* so this cannot happen (see its comment at :48-57) — these
two dialogs re-introduce it one level up, on the write path.
Also `BidirectionalBackfillDialog.cs:317` swallows a failed tag entirely: if every read fails the
dialog reports, in green, **"In sync — nothing to restore in either direction."**

**3. `SafeReadTimes` converts a failed read into "this server holds nothing" — on the card that
offers the restore.** `Forms/MainForm.cs:3203` `catch { return new List<DateTime>(); }`, no log.
The empty list feeds the analysis (whole-period gap, 0 % track) and `SyncPlanner` as the target
stream. The data table correctly says "could not load", while the same screen says the mirror is
0 % complete and N readings are missing. Same rule violation as 2, one level higher.

**4. The simple view's only write button has no re-entrancy guard.** `Forms/MainForm.cs:297-304`
— `_actionButtons` does not contain `btnRestore`, and its handler has no `_isBusy` check.
`btnBackfillPreview` (which is guarded) is Advanced-only. Two independent auditors found this.
Consequence chain, unverified end-to-end but code-confirmed at each link: a manual restore during
a headless scheduled run calls `ResetCts()` (`:1192`), which **disposes the CTS the scheduled
worker holds** and repoints `_cts` — that run can then no longer be cancelled by the Cancel button
or by `OnFormClosing`.

**5. A restore that could not be journaled still reports success.**
`Services/BackfillJournalService.cs:39` `catch { }` swallows every IO/serialization failure, so
`MainForm` sets `report.JournalId` unconditionally. Deployed to a read-only folder (the Phase 14
"single-folder deployment" plan), a restore writes to production, reports "N readings restored",
and is **permanently unrevertable** with no warning. This is the same swallow that hid the Phase 8
journal bug.

**6. The repeated autumn hour collapses every diff key.** `Services/SampleFilter.ToSecondTicks`
keys on LOCAL ticks, and the local clock repeats one hour each October, so two readings a real
hour apart share a key. Proven on this machine (W. Europe): `localTicksEqual=True`,
`dateTimeEquals=True`, `hashEqual=True`. A mirror outage confined to the repeated hour therefore
reads as "in sync" forever. Related: `MainForm.cs:2301-2303` builds the verify window with
`AddSeconds(±1)` on ambiguous local times, so `vStart` can resolve LATER than `vEnd` → empty
read → batch reported failed and **not journaled** although the write landed.
*Writes still land at correct instants* — `ToApi(FromApi(x)) == x` was proven exact inside the
ambiguous hour — so this is wrong-numbers + unrevertable, not corrupted data.

**7. A point the mirror recorded NOTHING for is reported "no difference found", in green.**
`Services/CoverageScanner.cs:236` `if (lackFilled == 0) return 0;` — the estimate bails out on a
total one-server outage, so the row sorts as healthy and is excluded from the totals, while
opening it offers to restore every reading. The worst case the tool exists to find is the one
case the list hides. (Code confirmed; the green verdict follows from `InSync` → `SortRank 4`.)

**8. Saving the scheduler settings can silently widen the next unattended run to `*`.**
`Forms/SchedulerSettingsDialog.cs:302` rebuilds `ScheduleTagList` from a list box that is empty
whenever nothing has been browsed, while `ScheduleUseTagList` stays true → `MainForm.cs:2676`
degrades to the mask. "Only these 3 points" silently becomes every shared point.

## How each was fixed, and what it measured afterwards

**H1 — the planner now tests SYMMETRY, not just the match rate.** Swept all 72 shared points
over 30 days (`tools/probes/probe-planner-sweep.ps1`) and the two populations turned out not to
overlap at all — they are **250x apart**:

| | points | smaller one-sided share | match rate |
|---|---|---|---|
| genuinely aligned | 3 | **0.01 %** | 100.0 % |
| independent collectors | 14 | **2.54 – 9.18 %** | 90.8 – 97.5 % |

All 14 were above the 90 % match bar and were being exact-diffed. Raising the match threshold
cannot separate them — 97.5 % is higher than several healthy pairs. The signal is that both
servers hold a similar share the other lacks, which no aligned pair does. `SyncPlan.OneSidedShare`
is now `min(srcOnly, tgtOnly)` and aligned requires it `<= 1 %` as well.

Measured on the same live window, before → after:

| | → mirror | → main | total |
|---|---|---|---|
| before | 73,144 | 73,526 | 146,670 |
| after | 37,015 | 37,487 | **74,502** |

**72,168 phantom writes prevented — about half of everything a full bidirectional restore would
have written.** Exact-diff points 17 → 3; points wanting >2 % copied in BOTH directions 14 → 0.
And the repair capability is intact: `STAT6.BHKW_01_GAS.F_CV` still reports exactly **5 → mirror,
3 → main**, unchanged, which is the Phase 12d verified value.

**H6 — keys are UTC now, and the effect is honest: no number moved.** `ToSecondTicks` converts to
UTC before truncating, so the repeated autumn hour can no longer collapse two instants onto one
key, and `ExecuteBackfill` sorts by the real instant so the verify window cannot invert. Re-measured
live: identical results on both a DST-free window and a year-long window spanning two change-overs.
That is expected — a collision needs two readings on the same local SECOND in both halves of the
repeated hour, which is rare at this point's ~140 s cadence and certain at 1 s. The value is that
it can no longer happen, not a number that moved. Journal ticks on disk are unchanged (the journal
passes an already-UTC value, which is used as-is), so legacy entries still revert identically —
pinned by a test.

**H2/H3 — a failed read is no longer an empty server.** `SafeReadTimes` returns `null`, which the
existing callers already treat as "not read" and exclude from analysis, feasibility and planning;
both preview dialogs let the failure surface instead of substituting an empty list, mark the row
`err`, untick and lock it, and count points they could not compare so "In sync" is never claimed
over failed reads.

**H4 — one operation at a time.** `btnRestore` joined `_actionButtons`; every write entry point
now calls `BlockedByRunningOperation()`, which checks `_scheduledRunActive` as well as `_isBusy`.
The scheduled flag is held for the WHOLE unattended run — including the browses before the first
write and the gap between two directions where `ExecuteBackfill`'s own `finally` had already
cleared `_isBusy`. `ResetCts` now traces a warning if it ever runs while something is in flight,
so a future entry point that forgets its guard announces itself instead of silently making a
production write uncancellable.

**H5** returns `bool`; the caller reports the failure and leaves `JournalId` unset rather than
claiming success. **H7** counts a total one-server outage as everything the other server holds.
**H8** preserves the saved tag list when the picker was never populated and refuses to save an
empty explicit selection.

Tests: **127 green** (was 118), including the symmetric-shortfall case, its one-sided mirror image
that must NOT be caught, the DST key collision, and the journal-frame pin.

## MEDIUM — recorded, not urgent

- **No `Settings.Default.Upgrade()` anywhere.** The Phase 14 version bump to 1.0.0 will silently
  discard every persisted setting (servers, language, view mode, schedule). Fix before packaging.
- **Destructive confirmations are hardcoded English** and name servers by their internal
  `Primary`/`Secondary` labels: `MainForm.cs:350-356` (the startup auto-write confirmation),
  `BackfillHistoryDialog.cs:186` (permanent delete), the restore progress line
  (`MainForm.cs:2205`, which in German reads "Tag 3 / 12" = *day* 3 of 12).
- **The overview is never marked stale** when the date range changes after a scan, and a cancelled
  scan leaves the previous scan on screen presented as current (`MainForm.cs:488-503`, `:617`).
- **`ValueChart.OnPaint` still calls `ComputeRange` uncached** (`:169`) while the envelope beside
  it is cached — the 12e fix stopped one line short; it walks both full series on every hover.
- **Overview buckets in the UTC span, the detail card segments in the local span**
  (`HistorianDataService.cs:225` vs `MainForm.cs:3094`) — across a DST change the two cards divide
  the same window into different grids. Sub-percent normally; it is the B2 class.

## Fixed in this audit

- `GapAnalysisService.Analyze` never set `TotalSamples` on its `Count < 2` path — a one-reading
  server returned `HasData=true, CoverageRatio=1.0, TotalSamples=0`. **This test was red on
  master and nobody could see it**, because the suite could not run outside Visual Studio.
- `HistorianDataService.WriteFloatSamples` was the one write/delete path that never called
  `ToApi` — no caller today, which is exactly why it looked correct. Now converts.
- `TagOverviewList` allocated an undisposed `Font` **and** `StringFormat` per visible row per
  paint, on a list that repaints on hover, wheel and scroll — a continuous GDI handle leak.
- `tools/run-tests.ps1` — the suite now runs headlessly in ~0.2 s (see below).

## Dead code — verified by grep across product, Designer files, DemoDataService and tests

`UI/Controls/CoverageBar.cs` (199 lines, compiled, no construction site) · `Models/ServerStats.cs`
(16 lines) · `HistorianDataService.TagExists / ReadInterpolated / ReadRaw / VerifyWrite` (each
referenced only by its `DemoDataService` override; `VerifyWrite` is the count-based check Phase 8
replaced *for producing false passes*) · `IntervalBuilder.BuildCopyableSegments` and
`SplitByFeasibility` (tests only) · `Loc.LanguageChanged` (raised, zero subscribers) ·
`GapAnalysisResult.SampleTimes` (written, never read — and it pins the full timestamp list in
`_lastPrimaryResult`).

**Two of these contradict the docs**: `architecture.md` credits `IntervalBuilder.SplitByFeasibility`
to the analysis worker and presents `Loc.LanguageChanged` as the language mechanism. Both are
unused. Fix the docs when the code is removed.

## Also found

`ReadRaw` and `ReadInterpolated` declare `out errors` and never check it — the same "an errored
read looks like no data" hole `ReadRawInRange` was fixed for. Both are currently unused, so this
is latent, but they must not be adopted as-is.

**`tools/probes/probe-crosscheck.ps1` verifies a superseded definition.** Its `Get-SegmentCoverage`
computes *segments-touched*, which Phase 12e replaced with share-of-best. It printed
100.0 % / 100.0 % where the app now reports something else — so it would "confirm" a number the app
no longer claims. Update it before trusting it again. Its raw counts and planner numbers are still
valid, and they re-confirmed Phase 12d exactly: **10,080 / 10,078 raw, 5 → mirror, 3 → main.**

## Running the tests (no Visual Studio needed)

```
powershell -NoProfile -File tools\run-tests.ps1        # 118 tests, ~0.2 s
```

First run fetches MSTest framework + adapter from nuget.org into `tools/.testpkgs/` (gitignored)
and drops the two framework DLLs into `lib/`. **No .csproj change was needed** — the package's
assembly version is already the `14.0.0.0` the project references; they simply were not on disk.
