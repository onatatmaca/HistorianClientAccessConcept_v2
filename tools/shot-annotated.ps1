param(
  [Parameter(Mandatory=$true)][string]$Out,
  # Controls to mark, in order, by their caption. "1=Connect;2=Login…" numbers them explicitly;
  # plain "Connect;Login…" numbers them 1..n in the order given.
  [string]$Mark = "",
  # Buttons to press (by caption) before capturing, e.g. "Login…". FlatButton derives from
  # Button, so BM_CLICK works - unlike synthetic mouse messages, which this app ignores.
  [string]$Press = "",
  # Capture a modal dialog instead of the main window, matched on its title.
  [string]$Dialog = "",
  # Controls to press INSIDE that dialog before capturing, by caption. CheckBox is also a
  # BUTTON-class window, so this ticks boxes as well as pressing buttons.
  [string]$DialogPress = "",
  [int]$SettleMs = 14000,
  [int]$AfterPressMs = 1500,
  [switch]$German
)
# Screenshot the app with numbered callouts drawn ON the real controls.
#
# The callout positions are not eyeballed: the script enumerates the live child windows and
# reads each control's actual rectangle, so a marker can never drift away from the button it
# describes when the layout changes. Re-run it after any UI change and the manual is correct
# again.
#
# Capture is PrintWindow(PW_RENDERFULLCONTENT) - it needs neither focus nor the foreground and
# cannot photograph the user's own desktop. Clicks are BM_CLICK messages, so the real mouse is
# never moved.
#
# ASCII only (PS 5.1 reads a BOM-less .ps1 as ANSI).
#
#   powershell -NoProfile -File tools\shot-annotated.ps1 -Out docs\img\01.png `
#       -Mark "Connect;Login…;Check for missing data"

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $repo "bin\DebugDemo\HistorianSyncTool.exe"
if (-not (Test-Path $exe)) { throw "Not built: $exe" }

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;
using System.Drawing;using System.Drawing.Imaging;
public class Ui{
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
 [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int m);
 [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 delegate bool EnumProc(IntPtr h, IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct R{public int L,T,Rr,B;}

 public class Ctl{ public IntPtr H; public string Cls; public string Text; public Rectangle Rect; }

 /// Every visible child of root, with its rectangle relative to root's window box - the same
 /// frame the screenshot is in, so a marker lands exactly on the control.
 public static List<Ctl> Children(IntPtr root){
  R rr; GetWindowRect(root, out rr);
  var list = new List<Ctl>();
  EnumChildWindows(root,(h,p)=>{
   if(!IsWindowVisible(h)) return true;
   var c=new StringBuilder(128); GetClassName(h,c,128);
   var t=new StringBuilder(512); GetWindowText(h,t,512);
   R r; GetWindowRect(h,out r);
   list.Add(new Ctl{H=h,Cls=c.ToString(),Text=t.ToString(),
     Rect=new Rectangle(r.L-rr.L, r.T-rr.T, r.Rr-r.L, r.B-r.T)});
   return true;},IntPtr.Zero);
  return list;
 }

 public static IntPtr FindDialog(uint pid, string title){
  IntPtr found = IntPtr.Zero;
  EnumWindows((h,p)=>{
   uint wpid; GetWindowThreadProcessId(h, out wpid);
   if(wpid!=pid || !IsWindowVisible(h)) return true;
   var t=new StringBuilder(512); GetWindowText(h,t,512);
   if(t.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase)>=0){ found=h; return false; }
   return true;},IntPtr.Zero);
  return found;
 }

 // POST, never SEND. A button that opens a MODAL dialog does not return from its click
 // handler until that dialog closes, so SendMessage would block this script forever.
 public static void Click(IntPtr btn){ PostMessage(btn, 0x00F5, IntPtr.Zero, IntPtr.Zero); } // BM_CLICK

 public static Bitmap Capture(IntPtr hwnd){
  R r; GetWindowRect(hwnd, out r);
  int w=r.Rr-r.L, h=r.B-r.T;
  var bmp=new Bitmap(w,h,PixelFormat.Format32bppArgb);
  using(var g=Graphics.FromImage(bmp)){
   IntPtr dc=g.GetHdc();
   try{ PrintWindow(hwnd,dc,0x2);} finally{ g.ReleaseHdc(dc);}    // PW_RENDERFULLCONTENT
  }
  return bmp;
 }
}
'@ -ReferencedAssemblies System.Drawing

$argList = @("--demo")
$proc = Start-Process $exe -ArgumentList $argList -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddMilliseconds(40000)
    while([DateTime]::UtcNow -lt $deadline){
        Start-Sleep -Milliseconds 400; $proc.Refresh()
        if($proc.MainWindowHandle -ne 0){ break }
    }
    if($proc.MainWindowHandle -eq 0){ throw "no window" }
    Start-Sleep -Milliseconds $SettleMs

    $main = $proc.MainWindowHandle
    $ctls = [Ui]::Children($main)

    if ($German) {
        # The EN/DE switch is a Label, not a Button, so BM_CLICK does not apply; the language is
        # a persisted setting instead. Left unimplemented rather than silently producing an
        # English screenshot labelled German.
        throw "-German is not supported: set the language in the app and use -Dialog/-Mark against it."
    }

    foreach($caption in ($Press -split ';' | Where-Object { $_.Trim() })){
        $c = $ctls | Where-Object { $_.Text -eq $caption.Trim() -and $_.Cls -match 'BUTTON' } | Select-Object -First 1
        if(-not $c){ throw "button not found: '$caption'" }
        [Ui]::Click($c.H)
        Start-Sleep -Milliseconds $AfterPressMs
    }

    $target = $main
    if ($Dialog) {
        $target = [Ui]::FindDialog([uint32]$proc.Id, $Dialog)
        if($target -eq [IntPtr]::Zero){ throw "dialog not found: '$Dialog'" }
        $ctls = [Ui]::Children($target)

        foreach($caption in ($DialogPress -split ';' | Where-Object { $_.Trim() })){
            $c = $ctls | Where-Object { $_.Text -eq $caption.Trim() -and $_.Cls -match 'BUTTON' } | Select-Object -First 1
            if(-not $c){ throw "control not found in dialog: '$caption'" }
            [Ui]::Click($c.H)
            Start-Sleep -Milliseconds $AfterPressMs
            # The dialog resizes when the extra login appears, so re-read the layout.
            $ctls = [Ui]::Children($target)
        }
    }

    $bmp = [Ui]::Capture($target)

    # --- markers -------------------------------------------------------------------------
    if ($Mark) {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'ClearTypeGridFit'
        $accent = [System.Drawing.Color]::FromArgb(214,69,42)
        $penBox = New-Object System.Drawing.Pen($accent, 2.4)
        $brFill = New-Object System.Drawing.SolidBrush($accent)
        $brTxt  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $font   = New-Object System.Drawing.Font("Segoe UI", 11.5, [System.Drawing.FontStyle]::Bold)

        $n = 0
        foreach($spec in ($Mark -split ';' | Where-Object { $_.Trim() })){
            $spec = $spec.Trim()
            if($spec -match '^(\d+)=(.*)$'){ $num=[int]$matches[1]; $cap=$matches[2] } else { $n++; $num=$n; $cap=$spec }

            # Optional placement hint. Straddling the LEFT edge is right for the buttons in the
            # side panel, but on a full-width section header it covers the first letter of the
            # caption, so those ask for "@right".
            $side = 'left'
            if($cap -match '^(.*)@(left|right)$'){ $cap=$matches[1]; $side=$matches[2] }

            if($cap -match '^#(.+):(\d+)$'){
                # Some controls expose no window text at all - the server address fields are
                # ComboBoxes whose text lives somewhere GetWindowText cannot reach - so they are
                # addressed as #<class-fragment>:<index in creation order> instead.
                $clsFrag = $matches[1]; $idx = [int]$matches[2]
                $c = ($ctls | Where-Object { $_.Cls -match $clsFrag })[$idx]
            } else {
                $c = $ctls | Where-Object { $_.Text -eq $cap } | Select-Object -First 1
            }
            if(-not $c){ Write-Host "  (no control matching '$cap' - marker skipped)" -ForegroundColor Yellow; continue }
            $r = $c.Rect
            $g.DrawRectangle($penBox, $r.X-3, $r.Y-3, $r.Width+6, $r.Height+6)
            # The badge STRADDLES the control's left edge. Placing it fully outside pushed it
            # into whatever sits alongside - on the left-hand panel that was the measurement
            # point list, and the badges covered the point names they were meant to point at.
            # Straddling keeps a marker attached to its own control whatever the layout does.
            $d = 30
            $bx = if($side -eq 'right'){ $r.X + $r.Width - [int]($d/2) }
                  else { [Math]::Max(2, $r.X - [int]($d/2)) }
            $by = $r.Y + [Math]::Max(0, [int](($r.Height - $d)/2))
            $g.FillEllipse($brFill, $bx, $by, $d, $d)
            $sz = $g.MeasureString([string]$num, $font)
            $g.DrawString([string]$num, $font, $brTxt, $bx + ($d - $sz.Width)/2, $by + ($d - $sz.Height)/2)
        }
        $g.Dispose(); $penBox.Dispose(); $brFill.Dispose(); $brTxt.Dispose(); $font.Dispose()
    }

    if(-not [IO.Path]::IsPathRooted($Out)){ $Out = Join-Path (Get-Location) $Out }
    $dir = Split-Path $Out -Parent
    if($dir -and -not (Test-Path $dir)){ New-Item -ItemType Directory $dir -Force | Out-Null }
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("saved {0}  ({1}x{2})" -f $Out, [Ui]::Capture($target).Width, "")
}
finally {
    if($proc -and -not $proc.HasExited){ $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 700
                                         if(-not $proc.HasExited){ $proc.Kill() } }
}
