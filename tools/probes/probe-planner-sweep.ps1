param(
  [string]$From = "2026-04-27 00:00:00",
  [string]$To   = "2026-05-27 00:00:00",
  [int]$MaxTags = 200
)
# Does SyncPlanner pick the right MODE for every shared point, or only for most of them?
#
# The planner has one job that matters: decide whether the two servers carry the SAME stream
# (so an exact-second diff finds genuine isolated misses) or two INDEPENDENT collector streams
# (where an exact diff is meaningless - the same values are logged seconds apart, and copying
# the difference interleaves both streams into the archive permanently). It decides on the
# exact-second match rate, aligned at >= 90 %.
#
# Measured on one point over a year that threshold sat at 90.6 % - just inside - and the planner
# then proposed 21,306 readings to the mirror AND 19,746 back to the main server. Two servers
# cannot each be missing ~20 k readings the other holds. This sweep asks how common that is.
#
# Reads every shared point and prints, per point: readings per side, match rate, the mode taken,
# and what a restore would write IN BOTH DIRECTIONS. The last column is the tell:
#   symmetric large copies in both directions = independent streams misread as aligned.
#
# READ-ONLY. ASCII only.
#
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File tools\probes\probe-planner-sweep.ps1

. "$PSScriptRoot\_connect.ps1"

$From = Get-Date $From
$To   = Get-Date $To

$main   = Connect-Historian $MAIN
$mirror = Connect-Historian $MIRROR
$data   = New-DataService

# ITags.Query's out-parameter will not bind from PowerShell - BrowseTags wraps it.
$mainTags   = @($data.BrowseTags($main,   "*")   | ForEach-Object { $_.Name })
$mirrorTags = @($data.BrowseTags($mirror, "*")   | ForEach-Object { $_.Name })
$set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($t in $mirrorTags) { [void]$set.Add($t) }
$shared = @($mainTags | Where-Object { $set.Contains($_) } | Sort-Object)
if ($shared.Count -gt $MaxTags) { $shared = $shared[0..($MaxTags-1)] }

Write-Output ("window {0:yyyy-MM-dd} -> {1:yyyy-MM-dd}   shared points: {2}" -f $From, $To, $shared.Count)
Write-Output ""
Write-Output ("{0,-42} {1,8} {2,8} {3,7} {4,-9} {5,8} {6,8}" -f `
  "point", "main", "mirror", "match", "mode", "->mirror", "->main")
Write-Output ("-" * 100)

$floor = [TimeSpan]::FromSeconds(120)
$mult  = 2.0
$rows = @()

foreach ($tag in $shared) {
  try {
    $sM = $data.ReadRawInRange($main,   $tag, $From, $To)
    $sS = $data.ReadRawInRange($mirror, $tag, $From, $To)
  } catch {
    Write-Output ("{0,-42} read failed: {1}" -f $tag, $_.Exception.Message)
    continue
  }
  $tM = Get-Times $sM
  $tS = Get-Times $sS

  $toMirror = [HistorianSyncTool.Services.SyncPlanner]::Plan($tM, $tS, $From, $To, $floor, $mult)
  $toMain   = [HistorianSyncTool.Services.SyncPlanner]::Plan($tS, $tM, $From, $To, $floor, $mult)

  $mode = if ($toMirror.UsedExactDiff) { "exact" } else { "outage" }
  Write-Output ("{0,-42} {1,8} {2,8} {3,7:P1} {4,-9} {5,8} {6,8}" -f `
    $tag, $sM.Count, $sS.Count, $toMirror.MatchRate, $mode, $toMirror.ToCopy.Count, $toMain.ToCopy.Count)

  $rows += [pscustomobject]@{
    Tag = $tag; Main = $sM.Count; Mirror = $sS.Count; Match = $toMirror.MatchRate
    Exact = $toMirror.UsedExactDiff; ToMirror = $toMirror.ToCopy.Count; ToMain = $toMain.ToCopy.Count
  }
}

Write-Output ""
Write-Output "=== summary ==="
$exact = @($rows | Where-Object { $_.Exact })
Write-Output ("points: {0}   exact-diff: {1}   outage-fill: {2}" -f $rows.Count, $exact.Count, ($rows.Count - $exact.Count))

# The signature of a misread pair: exact-diff mode, yet BOTH directions want to copy a
# substantial share of the readings. A genuinely aligned pair has isolated misses, so at least
# one direction is near zero.
Write-Output ""
Write-Output "every exact-diff point, by the smaller of the two one-sided shares:"
Write-Output "(a genuinely aligned pair has ISOLATED misses, so one side is near zero;"
Write-Output " a large share on BOTH sides means the streams are merely offset)"
foreach ($r in ($exact | Sort-Object { [Math]::Min($_.ToMirror / [double]$_.Main, $_.ToMain / [double]$_.Mirror) })) {
  $a = $r.ToMirror / [double]$r.Main
  $b = $r.ToMain / [double]$r.Mirror
  Write-Output ("   min {0,7:P2}   match {1,6:P1}   {2,-42} ->mirror {3,6:P2}  ->main {4,6:P2}" -f `
    ([Math]::Min($a,$b)), $r.Match, $r.Tag, $a, $b)
}
Write-Output ""

$suspect = @($exact | Where-Object {
  $_.Main -gt 0 -and $_.Mirror -gt 0 -and
  ($_.ToMirror / [double]$_.Main) -gt 0.02 -and ($_.ToMain / [double]$_.Mirror) -gt 0.02
})
Write-Output ("exact-diff points wanting >2 % copied in BOTH directions: {0}" -f $suspect.Count)
foreach ($r in $suspect) {
  Write-Output ("   {0,-42} match {1,6:P1}  ->mirror {2} ({3:P1})  ->main {4} ({5:P1})" -f `
    $r.Tag, $r.Match, $r.ToMirror, ($r.ToMirror / [double]$r.Main), $r.ToMain, ($r.ToMain / [double]$r.Mirror))
}

$main.Disconnect(); $mirror.Disconnect()
