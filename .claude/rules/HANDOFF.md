# HANDOFF — after Phase 12e (2026-08-05)

Live continuation note. Delete this file when Phase 14 ships.

## Where things stand

**Phases 12d and 12e are complete, committed and verified live.** The build is green.
Next: **Phase 13 (audit)**, then **Phase 14 (v1.0 + installer + docs)** — see
[`roadmap.md`](roadmap.md), which now carries three measured items into the audit.

**The user has decided: BUNDLE the Proficy ClientAccess DLL** with the installer (they said
so on 2026-08-05). `lib/` already holds a local copy for building without the Historian
client; the installer just ships it beside the exe. Worth one sentence to them before
release about redistributing a GE assembly — their call, not a blocker.

**Do not re-litigate these — they were measured, not reasoned:**
- Completeness = share of everything recorded that this server holds, painted in proportion.
  "Segments touched" saturates at 100 % on any long window and made every row identical.
- A scan has NO time budget; it checks every point (273 over a year ≈ 20 s).
- The tables must never cache display strings — that was 1,006 MB for one point on x86.

## Build / verify commands

```
dotnet msbuild HistorianSyncTool.csproj /p:Configuration=Debug /p:Platform=x86 ^
  /p:OutputPath=bin\DebugDemo\ /p:ReferencePath=<repo>\lib /v:minimal /nologo
```

Always build to **`bin\DebugDemo`** — the user usually has their own instance running from
`bin\Debug`, which locks that output. Kill only processes whose command line contains
`DebugDemo`; `bin\Debug` is theirs.

Screenshots (never `CopyFromScreen` — it captures the user's own windows; these use
`PrintWindow(PW_RENDERFULLCONTENT)`, which needs neither focus nor the foreground):
- `scratchpad\shot.ps1 -Out name.png [-Demo] -WaitMs 26000`
- `scratchpad\click-shot.ps1 -Out name.png [-Demo|-Attach] -Type "x,y=text" -Clicks "x,y;x,y"`
  — clicks are PostMessage'd to the control under the point, so the real mouse is never moved.
  Coordinates are in the same frame as the screenshots. `-Attach` drives the already-running
  DebugDemo instance instead of starting one.
- `scratchpad\shot-dialog.ps1 -Match "Measured values"` — modal dialogs are separate HWNDs and
  are not covered by the main-window capture.

Probes now live IN THE REPO at **`tools/probes/`** with a README — the session scratchpad does
not survive a new session, and the old copies carried credentials inline. `_connect.ps1` reads
credentials from `bin\DebugDemo\HistorianSyncTool.exe.config` (gitignored) or `HIST_USER` /
`HIST_PASS`, and servers from `HIST_MAIN` / `HIST_MIRROR` (default .186 / .187, port 13000).
All READ-ONLY by rule; **ASCII only** (PowerShell 5.1 reads a `.ps1` as ANSI without a BOM, so
an em-dash is a parse error).

```
C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File tools\probes\<probe>.ps1
```

- `probe-onesided.ps1` — are the "not set up on this server" verdicts true?
- `probe-crosscheck.ps1` — every number the detail card prints vs an independent read
  (`-Tag`, `-From`, `-To`)

Still only in the session scratchpad, worth porting when next needed: `probe-scanner.ps1`,
`probe-pointcoverage.ps1`, `probe-live-count.ps1`, `probe-sparse.ps1`.

## Before launching the app against the live servers

Check `%LOCALAPPDATA%\HistorianSyncTool\...\user.config`: `ScheduleEnabled` and
`ScheduleRunOnStartup` must both be `False`, or auto-connect on startup will fire a real
unattended restore. They were False on 2026-08-05.

## Phase 14 — still open

.NET Framework 4.8 is installed on all office machines (confirmed by the user), so the
installer does not need to bundle it. **Still to ask before building the installer:** bundle
the Proficy ClientAccess DLL, or require the Historian client to be installed?

## Hard-won facts — do not relearn these

- **Verify against the Historian, never against the app.** Several "bugs" have turned out to be
  bugs in the probe, and the real ones only appeared against live plant data — demo data is too
  regular to expose them. `probe-crosscheck.ps1` is the pattern: read raw, run the planner
  yourself, compare with what the screen printed.
- PowerShell traps: a C# `ValueTuple`'s field NAMES do not exist at runtime (`$s.Time` is null —
  use `$s.Item1`); `[int]` ROUNDS, it does not truncate (use `[Math]::Floor`);
  `[Math]::Max(1, <long>)` binds to Int32 and overflows; `New-Object HashSet[string]` cannot take
  (collection, comparer) — construct with the comparer and `Add` in a loop; `ITags.Query`'s
  `out` parameter will not bind from PowerShell — use `HistorianDataService.BrowseTags`.
- `git commit -m @'…'@` does not parse inside a `;`-chained PowerShell command — write the
  message to a file and use `-F`.
- `Control.Visible` returns EFFECTIVE visibility — false for every child before the form is
  shown. Never read it for layout decisions; `_hiddenByViewMode` + `IsShown()` exist for this.
- A hidden ComboBox reports no selection at all. The selected point is explicit app state
  (`_pointPrimary` / `_pointSecondary`), never read back off a combo.
- **A server that lacks a point is not a server holding nothing.** Both cards must agree; see
  the Phase 12d entry in `known-issues.md`. A read of a nonexistent tag throws `InvalidTagname`.
- The overview estimate is calibrated against SyncPlanner — see the measured table in
  `known-issues.md`. Any change to it must be re-measured with `probe-sparse.ps1` on LIVE data.
- Internal labels `"Primary"` / `"Secondary"` are load-bearing (journal, ScheduleDirection,
  ExecuteBackfill). Rename on screen only, via `ServerNaming`.
