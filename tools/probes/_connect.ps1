# Shared connect helper for the probes. Dot-source it:  . "$PSScriptRoot\_connect.ps1"
#
# Probes exist to check the app's numbers against the HISTORIAN, independently of the app.
# They are READ-ONLY by rule: no probe in this folder may call Add or Delete.
#
# Credentials are NOT stored here. They are read from the built app's config
# (bin\DebugDemo\HistorianSyncTool.exe.config, gitignored) - the same file the app itself
# uses - or from HIST_USER / HIST_PASS if you would rather pass them in. This exists because
# ConfigurationManager inside a PowerShell host reads powershell.exe.config, not ours.
#
# Run with 32-bit PowerShell - the exe is x86:
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File <probe.ps1>

$ErrorActionPreference = "Stop"

$Repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Base = Join-Path $Repo "bin\DebugDemo"
if (-not (Test-Path (Join-Path $Base "HistorianSyncTool.exe"))) {
  throw "Build to bin\DebugDemo first (see .claude/rules/HANDOFF.md)."
}

[void][Reflection.Assembly]::LoadFrom((Join-Path $Base "Proficy.Historian.ClientAccess.API.dll"))
[void][Reflection.Assembly]::LoadFrom((Join-Path $Base "HistorianSyncTool.exe"))

function Get-HistorianCredentials {
  if ($env:HIST_USER) { return @{ User = $env:HIST_USER; Pass = $env:HIST_PASS } }
  $cfg = Join-Path $Base "HistorianSyncTool.exe.config"
  if (-not (Test-Path $cfg)) { return @{ User = ""; Pass = "" } }   # empty = Windows session
  $xml = [xml](Get-Content $cfg -Raw)
  $get = { param($k) ($xml.configuration.appSettings.add | Where-Object { $_.key -eq $k }).value }
  return @{ User = (& $get "HistorianUsername"); Pass = (& $get "HistorianPassword") }
}

function Connect-Historian {
  param([string]$Host_, [int]$Port = 13000)
  $cred = Get-HistorianCredentials
  [HistorianSyncTool.Services.ProficyEndpoint]::SetPortForNextConnect($Port)
  $p = New-Object Proficy.Historian.ClientAccess.API.ConnectionProperties
  $p.ServerHostName = $Host_
  $p.Username = $cred.User
  $p.Password = $cred.Pass
  $p.ServerCertificateValidationMode = [Proficy.Historian.ClientAccess.API.CertificateValidationMode]::None
  $c = New-Object Proficy.Historian.ClientAccess.API.ServerConnection $p
  # An IP fails WCF's DNS-identity check; this is the same lenient path the app takes.
  if ($Host_ -match '^\d+\.\d+\.\d+\.\d+$') {
    [HistorianSyncTool.Services.ProficyEndpoint]::PrepareForIp($c, $p)
  }
  $c.Connect()
  return $c
}

function New-DataService { param([int]$Retries = 3) New-Object HistorianSyncTool.Services.HistorianDataService $Retries }

# Times of a List<(DateTime,float,double)>: the ValueTuple field NAMES do not exist at
# runtime in PowerShell, so .Time is $null and .Item1 is the timestamp.
function Get-Times {
  param($samples)
  $t = New-Object 'System.Collections.Generic.List[datetime]'
  foreach ($s in $samples) { $t.Add($s.Item1) }
  return $t
}

$MAIN   = if ($env:HIST_MAIN)   { $env:HIST_MAIN }   else { "192.168.50.186" }
$MIRROR = if ($env:HIST_MIRROR) { $env:HIST_MIRROR } else { "192.168.50.187" }
