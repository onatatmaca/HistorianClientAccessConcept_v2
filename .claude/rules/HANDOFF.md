# HANDOFF — mid-Phase-12d (2026-08-05)

Live continuation note. Delete this file when Phase 14 ships.

## Where things stand

Committed through **`dd1306f`** ("Fix the two defects from the second review"). Working tree
has **uncommitted, half-finished Phase 12d work that does NOT compile yet** — see below.

`bin\Debug` = last full build. All screenshot/probe work builds to **`bin\DebugDemo`**
(`/p:OutputPath=bin\DebugDemo\`) because the user often has the app open, which locks
`bin\Debug`.

## Build / verify commands

```
dotnet msbuild HistorianSyncTool.csproj /p:Configuration=Debug /p:Platform=x86 ^
  /p:OutputPath=bin\DebugDemo\ /p:ReferencePath=<repo>\lib /v:minimal /nologo
```

Screenshot (never CopyFromScreen — it captures the user's own windows):
`scratchpad\shot.ps1 -Out name.png [-Demo] -WaitMs 26000`

Probes (32-bit PowerShell; the exe is x86). All READ-ONLY:
- `probe-scanner.ps1` — CoverageScanner against the demo pair (15 checks)
- `probe-pointcoverage.ps1` — PointCoverage arithmetic (16 checks)
- `probe-live-count.ps1` — live count-query correctness + scan timing
- `probe-sparse.ps1` — overview estimate vs SyncPlanner truth, per point
- `probe-b2.ps1` / `probe-b2-verify.ps1` — the two completeness definitions

Run: `/c/Windows/SysWOW64/WindowsPowerShell/v1.0/powershell.exe -NoProfile -File '<path>'`

Live servers 192.168.50.186 (main) / .187 (mirror), port 13000, `ormatic`/`orc`.
Credentials live in `bin\DebugDemo\HistorianSyncTool.exe.config` (gitignored). The probes
carry them inline because ConfigurationManager in a PowerShell host reads powershell.exe.config.

## Phase 12d — DONE so far (uncommitted)

- **U1** `UI/Controls/ValueChart.cs` rewritten: two stacked plots (main above, mirror below),
  shared value scale, per-plot missing shading, grid labels inside on white backing, `Large`
  property, `CopyTo(other)`.
- **U2** `Forms/ChartDialog.cs` NEW — enlarged, resizable, Escape/Close. Designer adds
  `lnkEnlarge` to the chart header.
- **U3** "Load data" buttons removed from the Designer (`btnReadPrimary`/`btnReadSecondary`
  fields, creation, `ApplyTexts` lines, caption row 56px→26px).
- **U6 (part)** `txtPrimary`/`txtSecondary` are now `ComboBox` (`MakeHostCombo`).
- **U7** back button is a `FlatButton` (was a faint LinkLabel); detail header 28→34px.
- Chart panel 168→214, timeline 236→200. Grid captions non-bold + AutoEllipsis (a 33-char
  point name was clipping to 32 with no ellipsis — that is what made ".F_CV" read as ".F_C").
- `UI/Loc.cs`: `chart.enlarge`, `chart.enlargedTitle`, `grid.emptyNotOnServer`,
  `grid.emptyNoReadings`.

## Phase 12d — REMAINING (do these first; the build is broken until they are done)

1. **csproj**: add `<Compile Include="Forms\ChartDialog.cs" />`.
2. **MainForm.cs**: add `lnkEnlarge_Click` →
   `using (var d = new ChartDialog(chart, _pointPrimary)) d.ShowDialog(this);`
3. **MainForm.cs**: delete `btnReadPrimary_Click` / `btnReadSecondary_Click` and remove
   `btnReadPrimary, btnReadSecondary` from the `_actionButtons` list (they no longer exist).
4. **U4**: in `ApplyViewMode`, stop hiding `btnCompare` and `btnSyncScroll` — the user wants
   both in the simple view. (Delete the two `SetShown(...)` lines so they stay visible.)
5. **U5**: two fields `_emptyMsgPrimary` / `_emptyMsgSecondary`, set in `ShowSelectedPoint`
   and the read methods; the grid `Paint` handlers in the Designer draw them instead of the
   fixed `Loc.T("grid.emptyPrimary")`. Three states:
   no point → `grid.emptyPrimary`; point not on that server → `grid.emptyNotOnServer`;
   point present but zero readings → `grid.emptyNoReadings`.
   (`_pointOnMain` / `_pointOnMirror` already exist and are set in `OpenPoint`.)
6. **U6 (rest)**: `Settings.ServerHistory` (string, semicolon-separated). Load into both
   combos' `Items` at startup; after a successful connect add both hosts (de-duplicated,
   most-recent-first, cap ~10) and `Save()`.
7. Build → `shot.ps1 -Demo` → verify → also verify live → commit.

## Then: Phase 13 (audit) and Phase 14 (v1.0 + docs) — see roadmap.md

.NET Framework 4.8 is installed on all office machines (confirmed by the user), so the
installer does not need to bundle it. Still open: whether to bundle the Proficy ClientAccess
DLL or require the Historian client — ask before building the installer.

## Hard-won facts — do not relearn these

- **Verify against the Historian, never against the app.** Three "bugs" this session were
  bugs in my probe, and the two real ones only appeared against live plant data. Demo data
  is too regular to expose them.
- PowerShell traps: a C# `ValueTuple`'s field NAMES do not exist at runtime (`$s.Time` is
  null — use `$s.Item1`); `[int]` ROUNDS, it does not truncate (use `[Math]::Floor`);
  `[Math]::Max(1, <long>)` binds to Int32 and overflows.
- `Control.Visible` returns EFFECTIVE visibility — false for every child before the form is
  shown. Never read it for layout decisions; `_hiddenByViewMode` + `IsShown()` exist for this.
- A hidden ComboBox reports no selection at all. The selected point is explicit app state
  (`_pointPrimary` / `_pointSecondary`), never read back off a combo.
- The overview estimate is calibrated against SyncPlanner — see the measured table in
  `known-issues.md`. Any change to it must be re-measured with `probe-sparse.ps1` on LIVE data.
- Internal labels `"Primary"` / `"Secondary"` are load-bearing (journal, ScheduleDirection,
  ExecuteBackfill). Rename on screen only, via `ServerNaming`.
