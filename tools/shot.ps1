param(
  [string]$Out    = "shot.png",
  [switch]$Demo,
  [switch]$Attach,
  [int]$WaitMs    = 20000,
  [int]$SettleMs  = 2500,
  [string]$ExeDir = ""
)
# Screenshot the app's main window WITHOUT touching the user's desktop.
#
# Uses PrintWindow(PW_RENDERFULLCONTENT), which renders the window into a bitmap directly. It
# needs neither focus nor the foreground, so it cannot capture whatever the user happens to have
# on screen - unlike Graphics.CopyFromScreen, which must never be used here.
#
# This lived only in the session scratchpad and was lost with it; the probes were moved into the
# repo for exactly this reason, so it belongs here too.
#
#   powershell -NoProfile -File tools\shot.ps1 -Out overview.png -Demo
#   powershell -NoProfile -File tools\shot.ps1 -Out now.png -Attach
#
# NO SYNTHETIC CLICKING. It was tried (PostMessage of WM_MOUSEMOVE / WM_LBUTTONDOWN / UP /
# DBLCLK to the control under the point, walking to the deepest child) and it does NOT drive
# this app: neither the owner-drawn measurement-point list nor the EN/DE toggle responds, which
# matches the UI-Automation flakiness already recorded in known-issues.md. It was removed rather
# than left in place, because a capture step that silently does nothing produces a screenshot of
# the WRONG SCREEN and would quietly put it in the manual.
#
# To capture a screen that needs navigation: start the app yourself, click to where you want it,
# then run this with -Attach. That is reliable and takes seconds.
#
# ASCII only (PS 5.1 reads a BOM-less .ps1 as ANSI).

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ExeDir) { $ExeDir = Join-Path $repo "bin\DebugDemo" }
$exe = Join-Path $ExeDir "HistorianSyncTool.exe"

Add-Type -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class Shooter {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static void Capture(IntPtr hwnd, string path) {
        RECT r; GetWindowRect(hwnd, out r);
        int w = r.R - r.L, h = r.B - r.T;
        if (w <= 0 || h <= 0) throw new Exception("window has no size yet");
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp)) {
            IntPtr dc = g.GetHdc();
            // 0x2 = PW_RENDERFULLCONTENT: required for composited/DWM content, and the reason
            // this works on a window that is not in the foreground.
            try { PrintWindow(hwnd, dc, 0x2); } finally { g.ReleaseHdc(dc); }
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
"@ -ReferencedAssemblies System.Drawing, System.Windows.Forms

$proc = $null
if ($Attach) {
    $proc = Get-Process -Name HistorianSyncTool -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $proc) { throw "No running instance with a window. Drop -Attach to start one." }
} else {
    if (-not (Test-Path $exe)) { throw "Not built: $exe" }
    $argList = if ($Demo) { "--demo" } else { "" }
    $proc = Start-Process $exe -ArgumentList $argList -PassThru
    # Poll rather than sleeping the full budget - a demo window is usually up in a second or two.
    $deadline = [DateTime]::UtcNow.AddMilliseconds($WaitMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $proc.Refresh()
        if ($proc.HasExited) { throw "The app exited before a window appeared (exit $($proc.ExitCode))." }
        if ($proc.MainWindowHandle -ne 0) { Start-Sleep -Milliseconds $SettleMs; break }
    }
    if ($proc.MainWindowHandle -eq 0) { throw "No main window after ${WaitMs}ms." }
}

if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path (Get-Location) $Out }
[Shooter]::Capture($proc.MainWindowHandle, $Out)
Write-Output ("saved {0}  ({1})" -f $Out, $proc.MainWindowTitle)

if (-not $Attach) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 800
                    if (-not $proc.HasExited) { $proc.Kill() } }
