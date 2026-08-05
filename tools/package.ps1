# Builds the hand-out zip: one folder, unzip anywhere, double-click. No admin rights.
#
# .NET Framework 4.8 is already on every office machine, and the Proficy ClientAccess DLL is
# shipped beside the exe (decided 2026-08-05) so the Historian client does not have to be
# installed on a tester's PC.
#
# Deliberately NOT installed into Program Files: the app writes its scheduler audit log and -
# far more importantly - its revert journal next to the exe. In a read-only folder the journal
# silently fails, and a restore that cannot be journaled can never be undone. Keeping the whole
# thing in one user-writable folder removes that failure mode entirely.
#
# ASCII only (PS 5.1 reads a BOM-less .ps1 as ANSI).
#
#   powershell -NoProfile -File tools\package.ps1

[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OutDir        = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$lib  = Join-Path $repo 'lib'
if (-not $OutDir) { $OutDir = Join-Path $repo 'dist' }

# --- version comes from the assembly, never hand-typed twice ----------------------------
$asmInfo = Get-Content (Join-Path $repo 'Properties\AssemblyInfo.cs') -Raw
if ($asmInfo -notmatch 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)') {
    throw "Could not read AssemblyVersion from Properties\AssemblyInfo.cs"
}
$version = "$($matches[1]).$($matches[2]).$($matches[3])"
$name    = "HistorianSyncTool-$version"
Write-Host "Packaging $name ..." -ForegroundColor Cyan

# --- build ------------------------------------------------------------------------------
$stage = Join-Path $OutDir $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null

& dotnet msbuild (Join-Path $repo 'HistorianSyncTool.csproj') `
    /p:Configuration=$Configuration /p:Platform=x86 `
    /p:OutputPath=$stage /p:ReferencePath=$lib /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# --- prune build noise the tester does not need -----------------------------------------
Get-ChildItem $stage -Include *.pdb, *.xml -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

# --- the Proficy DLL must be there, or the app cannot connect at all ---------------------
$dll = Join-Path $stage 'Proficy.Historian.ClientAccess.API.dll'
if (-not (Test-Path $dll)) {
    $src = Join-Path $lib 'Proficy.Historian.ClientAccess.API.dll'
    if (-not (Test-Path $src)) { throw "Proficy DLL not found in lib\ - cannot package." }
    Copy-Item $src $stage
}

# --- config: ship the repo default, never a developer's credentials ----------------------
# bin\DebugDemo\HistorianSyncTool.exe.config holds real logins and is gitignored. The build
# writes the repo's App.config here, which is what we want; assert it to be sure.
$cfg = Join-Path $stage 'HistorianSyncTool.exe.config'
if (Test-Path $cfg) {
    $cfgText = Get-Content $cfg -Raw
    if ($cfgText -match 'HistorianPassword"\s+value="[^"]+"') {
        throw "The packaged config contains a password. Refusing to build a hand-out zip with credentials in it."
    }
}

# --- documentation ------------------------------------------------------------------------
$docs = Join-Path $repo 'docs'
foreach ($d in @('Handbuch-DE.docx', 'Manual-EN.docx', 'Manual-EN.html', 'Handbuch-DE.html')) {
    $p = Join-Path $docs $d
    if (Test-Path $p) { Copy-Item $p $stage }
    else { Write-Host "  (note: $d not built yet)" -ForegroundColor Yellow }
}

# --- zip ------------------------------------------------------------------------------------
$zip = Join-Path $OutDir "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)

Write-Host ""
Write-Host "Ready: $zip" -ForegroundColor Green
Get-ChildItem $stage | Select-Object Name, @{N='KB';E={[Math]::Round($_.Length/1KB)}} | Format-Table -AutoSize
Write-Host "Unzip anywhere and run HistorianSyncTool.exe - no admin rights needed."
