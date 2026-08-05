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

# Completeness, as Phase 12e defines it: per segment, the share of the readings the
# BETTER-SERVED server has. NOT "fraction of segments holding at least one reading" - that is
# what this probe used to compute, and it saturates at 100 % on any long window, so it happily
# "confirmed" 100.0 %/100.0 % for a track the app now reports differently. A probe that checks a
# superseded definition is worse than no probe.
function Get-SegmentCounts {
  param($times, [datetime]$from, [datetime]$to, [int]$n)
  $span = ($to - $from).Ticks
  $step = [Math]::Max([long]1, [long]($span / $n))     # [Math]::Max(1, <long>) binds Int32 and overflows
  $c = New-Object int[] $n
  foreach ($t in $times) {
    if ($t -lt $from -or $t -ge $to) { continue }
    $i = [Math]::Floor(($t - $from).Ticks / $step)      # [int] ROUNDS; Floor is required
    if ($i -ge 0 -and $i -lt $n) { $c[$i]++ }
  }
  return $c
}
function Get-ShareOfBest {
  param($mine, $other)
  $sumMine = [long]0; $sumBest = [long]0
  for ($i = 0; $i -lt $mine.Length; $i++) {
    $o = if ($i -lt $other.Length) { $other[$i] } else { 0 }
    $sumMine += $mine[$i]
    $sumBest += [Math]::Max($mine[$i], $o)
  }
  # Nothing recorded anywhere: neither server is incomplete relative to the other.
  if ($sumBest -eq 0) { return 0.0 }
  return $sumMine / [double]$sumBest
}

$cMain   = Get-SegmentCounts $tMain   $From $To $Segments
$cMirror = Get-SegmentCounts $tMirror $From $To $Segments
$shareMain   = Get-ShareOfBest $cMain   $cMirror
$shareMirror = Get-ShareOfBest $cMirror $cMain
Write-Output ("completeness main={0:P1}  mirror={1:P1}  ({2} segments, share-of-best)" -f `
  $shareMain, $shareMirror, $Segments)

# Computed independently above, then checked against the app's own PointCoverage. The probe must
# not simply CALL the app - that would prove nothing - so it does both and reports a divergence.
$pc = New-Object HistorianSyncTool.Services.PointCoverage
$pc.Tag = $Tag; $pc.Main = $cMain; $pc.Mirror = $cMirror; $pc.Scanned = $true
$dm = [Math]::Abs($pc.MainCoverage   - $shareMain)
$ds = [Math]::Abs($pc.MirrorCoverage - $shareMirror)
if ($dm -gt 1e-9 -or $ds -gt 1e-9) {
  Write-Output ("  MISMATCH vs PointCoverage: app main={0:P3} mirror={1:P3}" -f $pc.MainCoverage, $pc.MirrorCoverage)
} else {
  Write-Output "  agrees with PointCoverage (independent calculation, same answer)"
}

Write-Output ""
Write-Output "Compare these with what the app printed for the same point and window."

$main.Disconnect(); $mirror.Disconnect()
