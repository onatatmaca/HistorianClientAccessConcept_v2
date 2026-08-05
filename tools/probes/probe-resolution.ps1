param(
  [string]$Tag      = "STAT6.TEMPRL_01_BHKW02_SCALE.F_CV",
  [string]$FromText = "2024-08-05 06:56:05",
  [string]$ToText   = "2025-08-05 06:56:05",
  [int]$Segments    = 600
)
# Why does the all-points list show every point at ~100% green while the detail card for the
# same point is almost entirely red? Measures the three competing definitions side by side on
# ONE point and window, from raw data. READ-ONLY.
. "$PSScriptRoot\_connect.ps1"

# Separate names on purpose: a [string]-typed param KEEPS its type constraint, so
# "$From = Get-Date $From" coerces the parsed DateTime straight back to a string and the
# later ($To - $From) fails as an Int32 subtraction.
[datetime]$From = Get-Date $FromText
[datetime]$To   = Get-Date $ToText

$main   = Connect-Historian $MAIN
$mirror = Connect-Historian $MIRROR
$data   = New-DataService

Write-Output ("{0}  {1:yyyy-MM-dd} -> {2:yyyy-MM-dd}  ({3} segments of {4:n1} h)" -f `
  $Tag, $From, $To, $Segments, (($To - $From).TotalHours / $Segments))

$sw = [Diagnostics.Stopwatch]::StartNew()
$tMain   = Get-Times $data.ReadRawInRange($main,   $Tag, $From, $To)
$tMirror = Get-Times $data.ReadRawInRange($mirror, $Tag, $From, $To)
Write-Output ("raw counts   main={0:n0}  mirror={1:n0}   (read in {2:n1}s)" -f $tMain.Count, $tMirror.Count, $sw.Elapsed.TotalSeconds)

function Get-SegmentCounts {
  param($times, [datetime]$from, [datetime]$to, [int]$n)
  $span = ($to - $from).Ticks
  $step = [Math]::Max([long]1, [long]($span / $n))
  $c = New-Object int[] $n
  foreach ($t in $times) {
    if ($t -lt $from -or $t -ge $to) { continue }
    $i = [Math]::Floor(($t - $from).Ticks / $step)
    if ($i -ge 0 -and $i -lt $n) { $c[$i]++ }
  }
  return $c
}
$cMain   = Get-SegmentCounts $tMain   $From $To $Segments
$cMirror = Get-SegmentCounts $tMirror $From $To $Segments

# (1) What the app draws today: a segment is "covered" if it holds >= 1 reading.
$fillMain = 0; $fillMirror = 0; $bothEmpty = 0
for ($i = 0; $i -lt $Segments; $i++) {
  if ($cMain[$i]   -gt 0) { $fillMain++ }
  if ($cMirror[$i] -gt 0) { $fillMirror++ }
  if ($cMain[$i] -eq 0 -and $cMirror[$i] -eq 0) { $bothEmpty++ }
}
Write-Output ""
Write-Output ("(1) segment-touched  main={0:P1}  mirror={1:P1}   both-empty segments={2}" -f `
  ($fillMain / $Segments), ($fillMirror / $Segments), $bothEmpty)
Write-Output "    <- this is why every row looks the same: one reading paints a whole segment green"

# (2) Proposed: how much of everything recorded for this point does each server hold?
$sumMain = 0L; $sumMirror = 0L; $sumBest = 0L
for ($i = 0; $i -lt $Segments; $i++) {
  $sumMain   += $cMain[$i]
  $sumMirror += $cMirror[$i]
  $sumBest   += [Math]::Max($cMain[$i], $cMirror[$i])
}
if ($sumBest -gt 0) {
  Write-Output ("(2) share-of-best    main={0:P1}  mirror={1:P1}   (best total {2:n0})" -f `
    ($sumMain / $sumBest), ($sumMirror / $sumBest), $sumBest)
}

# (3) What a restore would actually write - the only number that must drive a write.
$floor = [TimeSpan]::FromSeconds(120); $mult = 2.0
$toMirror = [HistorianSyncTool.Services.SyncPlanner]::Plan($tMain, $tMirror, $From, $To, $floor, $mult)
$toMain   = [HistorianSyncTool.Services.SyncPlanner]::Plan($tMirror, $tMain, $From, $To, $floor, $mult)
Write-Output ("(3) SyncPlanner      -> mirror {0:n0}   -> main {1:n0}    exact-diff? {2}/{3}  match {4:P1}" -f `
  $toMirror.ToCopy.Count, $toMain.ToCopy.Count, $toMirror.UsedExactDiff, $toMain.UsedExactDiff, $toMirror.MatchRate)

# How much of the window would the planner's copies PAINT if each is snapped to a segment?
$hitMirror = New-Object bool[] $Segments
$span = ($To - $From).Ticks
$step = [Math]::Max([long]1, [long]($span / $Segments))
foreach ($t in $toMirror.ToCopy) {
  if ($t -lt $From -or $t -ge $To) { continue }
  $i = [Math]::Floor(($t - $From).Ticks / $step)
  if ($i -ge 0 -and $i -lt $Segments) { $hitMirror[$i] = $true }
}
$painted = 0; foreach ($h in $hitMirror) { if ($h) { $painted++ } }
Write-Output ("    those {0:n0} readings touch {1} of {2} segments = {3:P1} of the track painted RED" -f `
  $toMirror.ToCopy.Count, $painted, $Segments, ($painted / $Segments))
Write-Output "    <- this is why the detail card looks catastrophic at 100% coverage"

$main.Disconnect(); $mirror.Disconnect()
