# Runs the MSTest suite WITHOUT Visual Studio.
#
# Why this exists: the test project referenced the MSTest framework assemblies with no
# HintPath, so they only resolved inside a full VS install and the suite could not be run
# from a build box. It was therefore never part of any verification loop - and a genuinely
# red test (GapAnalysisService leaving TotalSamples=0 on the single-sample path) sat
# unnoticed. Runtime is ~0.2 s; there is no excuse not to run it on every change now.
#
# Needs: dotnet msbuild + vstest.console.exe (ships with VS 2022 Build Tools).
# First run downloads the two MSTest packages from nuget.org; after that it is offline.
#
# ASCII ONLY - PowerShell 5.1 reads a .ps1 without a BOM as ANSI, so an em-dash here is a
# parse error, not a typo.
#
#   powershell -NoProfile -File tools\run-tests.ps1

[CmdletBinding()]
param(
    [string] $Configuration = 'Debug',
    [string] $MsTestVersion = '3.6.4'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$pkgDir = Join-Path $repo 'tools\.testpkgs'
$libDir = Join-Path $repo 'lib'

function Get-NuGetPackage([string] $Name, [string] $Version) {
    $dest = Join-Path $pkgDir $Name
    if (Test-Path (Join-Path $dest '_ok')) { return $dest }

    Write-Host "Downloading $Name $Version ..." -ForegroundColor Cyan
    if (-not (Test-Path $pkgDir)) { New-Item -ItemType Directory $pkgDir | Out-Null }
    $zip = Join-Path $pkgDir "$Name.zip"
    # Tls12 is not the default on stock PS 5.1; nuget.org refuses anything older.
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/$Name/$Version" `
        -OutFile $zip -UseBasicParsing -TimeoutSec 180
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    Remove-Item $zip -Force
    New-Item -ItemType File (Join-Path $dest '_ok') | Out-Null
    return $dest
}

function Find-VsTest {
    $roots = @(
        "$env:ProgramFiles\Microsoft Visual Studio\2022",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022"
    )
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        $hit = Get-ChildItem $r -Recurse -Filter 'vstest.console.exe' -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "vstest.console.exe not found. Install the VS 2022 Build Tools (Testing workload)."
}

# --- MSTest framework assemblies -> lib\ so ReferencePath resolves them -----------------
# Their AssemblyVersion is 14.0.0.0, exactly what the .csproj already references, so no
# project file change is needed.
$fw = Get-NuGetPackage 'MSTest.TestFramework' $MsTestVersion
if (-not (Test-Path $libDir)) { New-Item -ItemType Directory $libDir | Out-Null }
foreach ($dll in @('Microsoft.VisualStudio.TestPlatform.TestFramework.dll',
                   'Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions.dll')) {
    $srcDll = Join-Path $fw "lib\net462\$dll"
    $dstDll = Join-Path $libDir $dll
    if (-not (Test-Path $dstDll)) { Copy-Item $srcDll $dstDll -Force }
}

# --- adapter: vstest needs it to discover [TestMethod] ----------------------------------
$adapter = Join-Path (Get-NuGetPackage 'MSTest.TestAdapter' $MsTestVersion) 'build\net462'

# --- build ------------------------------------------------------------------------------
# Output goes to bin\DebugTests, NEVER bin\Debug: the developer usually has their own
# instance running from bin\Debug and that locks the output.
$proj = Join-Path $repo 'HistorianSyncTool.Tests\HistorianSyncTool.Tests.csproj'
Write-Host "Building tests ..." -ForegroundColor Cyan
& dotnet msbuild $proj /p:Configuration=$Configuration /p:Platform=x86 `
    /p:OutputPath=bin\DebugTests\ /p:ReferencePath=$libDir /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Test project build failed." }

# --- run ---------------------------------------------------------------------------------
$testDll = Join-Path $repo 'HistorianSyncTool.Tests\bin\DebugTests\HistorianSyncTool.Tests.dll'
& (Find-VsTest) $testDll /TestAdapterPath:$adapter /Platform:x86 `
    /Framework:".NETFramework,Version=v4.8" /Logger:"console;verbosity=minimal"
exit $LASTEXITCODE
