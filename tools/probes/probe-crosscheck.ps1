param(
  [string]$Tag  = "STAT6.BHKW_01_GAS.F_CV",
  [string]$From = "2026-05-20 00:00:00",
  [string]$To   = "2026-05-27 00:00:00",
  [int]$Segments = 600
)
# Cross-checks every number the detail card prints for ONE point against an independent read
# of the Historian: raw counts, the planner's verdict in both directions, and the segment
# completeness both cards must agree on. READ-ONLY.
#
# This is the pattern for Phase 13: read raw, run the planner yourself, compare with the
# screen. Never compare the app against itself.
. "$PSScriptRoot\_connect.ps1"

$From = Get-Date $From
$To   = Get-Date $To

$main   = Connect-Historian $MAIN
$mirror = Connect-Historian $MIRROR
$data   = New-DataService

$sMain   = $data.ReadRawInRange($main,   $Tag, $From, $To)
$sMirror = $data.ReadRawInRange($mirror, $Tag, $From, $To)
Write-Output ("{0}  {1:yyyy-MM-dd} -> {2:yyyy-MM-dd}" -f $Tag, $From, $To)
Write-Output ("raw counts   main={0}  mirror={1}  (net {2})" -f $sMain.Count, $sMirror.Count, ($sMain.Count - $sMirror.Count))

$tMain   = Get-Times $sMain
$tMirror = Get-Times $sMirror

# The same floor/multiplier the app uses (app.config defaults).
$floor = [TimeSpan]::FromSeconds(120)
$mult  = 2.0
$toMirror = [HistorianSyncTool.Services.SyncPlanner]::Plan($tMain, $tMirror, $From, $To, $floor, $mult)
$toMain   = [HistorianSyncTool.Services.SyncPlanner]::Plan($tMirror, $tMain, $From, $To, $floor, $mult)
Write-Output ("planner      -> mirror: {0}   -> main: {1}" -f $toMirror.ToCopy.Count, $toMain.ToCopy.Count)
Write-Output ("             exact-diff? mirror={0} main={1}   match rate {2:P1}" -f `
  $toMirror.UsedExactDiff, $toMain.UsedExactDiff, $toMirror.MatchRate)

# Segment completeness: a segment counts as covered from ONE reading, which is exactly the
# rule the overview list and the timeline share.
function Get-SegmentCoverage {
  param($times, [datetime]$from, [datetime]$to, [int]$n)
  $span = ($to - $from).Ticks
  $step = [Math]::Max([long]1, [long]($span / $n))     # [Math]::Max(1, <long>) binds Int32 and overflows
  $fill = New-Object bool[] $n
  foreach ($t in $times) {
    if ($t -lt $from -or $t -ge $to) { continue }
    $i = [Math]::Floor(($t - $from).Ticks / $step)      # [int] ROUNDS; Floor is required
    if ($i -ge 0 -and $i -lt $n) { $fill[$i] = $true }
  }
  $c = 0; foreach ($f in $fill) { if ($f) { $c++ } }
  return $c / $n
}
Write-Output ("completeness main={0:P1}  mirror={1:P1}  ({2} segments)" -f `
  (Get-SegmentCoverage $tMain $From $To $Segments), (Get-SegmentCoverage $tMirror $From $To $Segments), $Segments)
Write-Output ""
Write-Output "Compare these with what the app printed for the same point and window."

$main.Disconnect(); $mirror.Disconnect()
