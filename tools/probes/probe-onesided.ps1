# Are the app's "this point is not set up on this server" verdicts TRUE? (READ-ONLY)
#
# The app refuses to plan a restore for a point only one server has. If its presence test were
# wrong it would hide a real point behind "not set up here" and silently stop offering a repair
# that IS possible - worse than the inflated number it replaced. So this asks the Historian
# directly: browse each server, query the exact name, and try to read it on the other side.
param(
  [string]$From = "2026-05-20 00:00:00",
  [string]$To   = "2026-05-27 00:00:00"
)
. "$PSScriptRoot\_connect.ps1"

$From = Get-Date $From
$To   = Get-Date $To

$main   = Connect-Historian $MAIN
$mirror = Connect-Historian $MIRROR
$data   = New-DataService

$tagsMain   = @($data.BrowseTags($main,   "*") | ForEach-Object { $_.Name })
$tagsMirror = @($data.BrowseTags($mirror, "*") | ForEach-Object { $_.Name })
$setMain   = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$setMirror = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($t in $tagsMain)   { [void]$setMain.Add($t) }
foreach ($t in $tagsMirror) { [void]$setMirror.Add($t) }

$shared     = @($tagsMain   | Where-Object { $setMirror.Contains($_) })
$mainOnly   = @($tagsMain   | Where-Object { -not $setMirror.Contains($_) })
$mirrorOnly = @($tagsMirror | Where-Object { -not $setMain.Contains($_) })

Write-Output ("browsed: main={0}  mirror={1}  shared={2}  main-only={3}  mirror-only={4}  union={5}" -f `
  $tagsMain.Count, $tagsMirror.Count, $shared.Count, $mainOnly.Count, $mirrorOnly.Count,
  ($tagsMain.Count + $mirrorOnly.Count))
Write-Output "  (the app's overview must show the union as its point count, and main-only + mirror-only as 'not on both servers')"

# A one-sided point: exact-name browse on both, then an actual read on both.
if ($mirrorOnly.Count -gt 0) {
  $probe = $mirrorOnly[0]
  Write-Output ""
  Write-Output ("mirror-only sample: {0}" -f $probe)
  foreach ($pair in @(@{n="main";c=$main}, @{n="mirror";c=$mirror})) {
    $hits = @($data.BrowseTags($pair.c, $probe) | ForEach-Object { $_.Name })
    Write-Output ("  exact browse on {0}: {1} hit(s)" -f $pair.n, $hits.Count)
    try   { Write-Output ("  read on {0}: {1} sample(s)" -f $pair.n, $data.ReadRawInRange($pair.c, $probe, $From, $To).Count) }
    catch { Write-Output ("  read on {0}: THREW - {1}" -f $pair.n, $_.Exception.Message) }
  }
}

# A shared point must read on BOTH - the control case.
if ($shared.Count -gt 0) {
  Write-Output ""
  Write-Output ("shared sample: {0}" -f $shared[0])
  foreach ($pair in @(@{n="main";c=$main}, @{n="mirror";c=$mirror})) {
    Write-Output ("  read on {0}: {1} sample(s)" -f $pair.n, $data.ReadRawInRange($pair.c, $shared[0], $From, $To).Count)
  }
}

# The dangerous direction: a point the app calls one-sided that DOES hold data on the other
# server would mean a real repair is being hidden. Must be zero.
Write-Output ""
Write-Output "one-sided points that DO return data on the other server (must be 0):"
$wrong = 0; $checked = 0
foreach ($t in ($mirrorOnly | Select-Object -First 40)) {
  $checked++
  try { $s = $data.ReadRawInRange($main, $t, $From, $To); if ($s.Count -gt 0) { $wrong++; Write-Output ("  MIRROR-ONLY but main holds {0}: {1}" -f $s.Count, $t) } } catch {}
}
foreach ($t in ($mainOnly | Select-Object -First 40)) {
  $checked++
  try { $s = $data.ReadRawInRange($mirror, $t, $From, $To); if ($s.Count -gt 0) { $wrong++; Write-Output ("  MAIN-ONLY but mirror holds {0}: {1}" -f $s.Count, $t) } } catch {}
}
Write-Output ("  checked {0}, wrong {1}" -f $checked, $wrong)

$main.Disconnect(); $mirror.Disconnect()
