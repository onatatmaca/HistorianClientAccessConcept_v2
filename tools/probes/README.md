# Probes — checking the app against the Historian

A probe answers one question: **is the number on screen true?** It talks to the Historian
directly and computes the answer itself, so it can disagree with the app. Comparing the app
against itself proves nothing, and demo data is too regular to expose the errors that matter —
every real defect found in this project so far only appeared against live plant data.

## Rules

- **READ-ONLY.** No probe here may call `IData.Add` or `IData.Delete`. This tooling runs
  against a production-shaped historian.
- **No credentials in the repo.** `_connect.ps1` reads them from the built app's own config
  (`bin\DebugDemo\HistorianSyncTool.exe.config`, gitignored), or from `HIST_USER` / `HIST_PASS`.
- Servers default to `192.168.50.186` / `.187`; override with `HIST_MAIN` / `HIST_MIRROR`.
- **ASCII only.** PowerShell 5.1 reads a `.ps1` as ANSI unless it has a BOM, so a stray em-dash
  becomes a parse error.

## Running

Build to `bin\DebugDemo` first, then use **32-bit** PowerShell — the exe is x86:

```
C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File tools\probes\<probe>.ps1
```

| Probe | Answers |
|---|---|
| `probe-onesided.ps1` | Are the "not set up on this server" verdicts true? Browse counts per server, an exact-name query for a one-sided point, and whether any one-sided point secretly holds data on the other server (must be 0 — otherwise a real repair is being hidden). |
| `probe-crosscheck.ps1` | For one point and window: raw counts per server, what `SyncPlanner` would copy in each direction, and segment completeness. Compare each with what the app printed. Takes `-Tag`, `-From`, `-To`. |

## PowerShell traps that have already cost time here

- A C# `ValueTuple`'s field **names do not exist at runtime**: `$s.Time` is `$null`; use
  `$s.Item1` (or `Get-Times`).
- `[int]` **rounds**; use `[Math]::Floor` for a bucket index.
- `[Math]::Max(1, <long>)` binds the Int32 overload and overflows — write `[long]1`.
- `New-Object HashSet[string]` will not take `(collection, comparer)`; construct with the
  comparer and `Add` in a loop.
- `ITags.Query`'s `out` parameter will not bind from PowerShell — use
  `HistorianDataService.BrowseTags`.
