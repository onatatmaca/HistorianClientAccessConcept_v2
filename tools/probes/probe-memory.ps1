param(
  [string]$Tag  = "STAT6.TEMPRL_01_BHKW02_SCALE.F_CV",
  [string]$From = "2024-08-05 00:00:00",
  [string]$To   = "2025-08-05 00:00:00"
)
# What does ONE point over a long window actually cost in memory, step by step?
#
# The process is x86 and dies around 1.2 GB, so this is a correctness question, not a
# performance one. Phase 12e measured the whole app at 1,006 MB -> 639 MB; this probe
# attributes the REMAINDER to individual structures so the next fix is aimed, not guessed.
#
# It reproduces exactly what opening one point does, in order:
#   1. ReadRawInRange per server            (what the tables bind to)
#   2. one GridRow-equivalent object per reading (MainForm.SamplesToGridRows)
#   3. GapAnalysisService.Analyze per server (which RE-READS the same window, and whose
#      result retains SampleTimes)
#   4. SyncPlanner.Plan in both directions
#
# READ-ONLY. ASCII only (PS 5.1 reads a BOM-less .ps1 as ANSI).
#
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File tools\probes\probe-memory.ps1

. "$PSScriptRoot\_connect.ps1"

$From = Get-Date $From
$To   = Get-Date $To
$proc = [System.Diagnostics.Process]::GetCurrentProcess()

function Show-Mem {
  param([string]$Label)
  # GetTotalMemory($true) forces a blocking collect, so what remains is RETAINED, not garbage.
  $managed = [GC]::GetTotalMemory($true) / 1MB
  $proc.Refresh()
  $priv = $proc.PrivateMemorySize64 / 1MB
  Write-Output ("{0,-46} managed {1,8:N1} MB   private {2,8:N1} MB" -f $Label, $managed, $priv)
}

Write-Output ("{0}   {1:yyyy-MM-dd} -> {2:yyyy-MM-dd}" -f $Tag, $From, $To)
Write-Output ""
Show-Mem "baseline (connected, nothing read)"

$main   = Connect-Historian $MAIN
$mirror = Connect-Historian $MIRROR
$data   = New-DataService

# --- 1. the read the tables bind to -----------------------------------------------------
$sMain = $data.ReadRawInRange($main, $Tag, $From, $To)
Show-Mem ("1. main read ({0:N0} readings)" -f $sMain.Count)

$sMirror = $data.ReadRawInRange($mirror, $Tag, $From, $To)
Show-Mem ("1. + mirror read ({0:N0} readings)" -f $sMirror.Count)

# --- 2. one row object per reading, as SamplesToGridRows does ---------------------------
# GridRow is private to MainForm, so this stands in for it with the same three fields.
# It measures the COST OF THE EXTRA OBJECT, which is the point.
Add-Type -TypeDefinition @"
public class RowProbe {
    public System.DateTime RawTime;
    public float RawValue;
    public double RawQuality;
}
"@ -ErrorAction SilentlyContinue

$rowsMain = New-Object 'System.Collections.Generic.List[RowProbe]' $sMain.Count
foreach ($s in $sMain) {
  $r = New-Object RowProbe
  $r.RawTime = $s.Item1; $r.RawValue = $s.Item2; $r.RawQuality = $s.Item3
  $rowsMain.Add($r)
}
Show-Mem "2. + one row object per main reading"

$rowsMirror = New-Object 'System.Collections.Generic.List[RowProbe]' $sMirror.Count
foreach ($s in $sMirror) {
  $r = New-Object RowProbe
  $r.RawTime = $s.Item1; $r.RawValue = $s.Item2; $r.RawQuality = $s.Item3
  $rowsMirror.Add($r)
}
Show-Mem "2. + one row object per mirror reading"

# --- 3. the analysis, which reads the SAME window a second time -------------------------
# GapAnalysisService.Analyze cannot be called from PowerShell at all: its optional
# `DateTime evalFrom = default` parameters make member lookup throw "Encountered an invalid
# type for a default value". So measure the part that actually matters here - that the
# analysis issues a SECOND full read of the window the tables already hold - by repeating
# the read exactly as SafeReadTimes does (read, project to times, sort).
$reReadMain = $data.ReadRawInRange($main, $Tag, $From, $To)
$timesMain  = New-Object 'System.Collections.Generic.List[datetime]'
foreach ($s in $reReadMain) { $timesMain.Add($s.Item1) }
$timesMain = [System.Linq.Enumerable]::ToList([System.Linq.Enumerable]::OrderBy($timesMain, [Func[datetime,datetime]]{ param($t) $t }))
Show-Mem "3. + analysis re-read of main (2nd full read)"

$reReadMirror = $data.ReadRawInRange($mirror, $Tag, $From, $To)
$timesMirror  = New-Object 'System.Collections.Generic.List[datetime]'
foreach ($s in $reReadMirror) { $timesMirror.Add($s.Item1) }
Show-Mem "3. + analysis re-read of mirror (2nd full read)"

# --- 4. the planner, both directions ----------------------------------------------------
$tMain = Get-Times $sMain; $tMirror = Get-Times $sMirror
$floor = [TimeSpan]::FromSeconds(120); $mult = 2.0
$p1 = [HistorianSyncTool.Services.SyncPlanner]::Plan($tMain, $tMirror, $From, $To, $floor, $mult)
$p2 = [HistorianSyncTool.Services.SyncPlanner]::Plan($tMirror, $tMain, $From, $To, $floor, $mult)
Show-Mem "4. + SyncPlanner both directions"
Write-Output ("   planner: -> mirror {0:N0}   -> main {1:N0}   exact-diff {2}/{3}  match {4:P1}" -f `
  $p1.ToCopy.Count, $p2.ToCopy.Count, $p1.UsedExactDiff, $p2.UsedExactDiff, $p1.MatchRate)

$main.Disconnect(); $mirror.Disconnect()
Write-Output ""
Write-Output "Each line is cumulative and after a forced collect, so it is retained memory."
