param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [string[]]$ArgList = @(),
    [Parameter(Mandatory=$true)][string]$Out,
    [switch]$PrintWindow,
    [int]$WaitMs = 1500
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public class Win32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int X, int Y, int cx, int cy, uint flags);
}
"@

[void][Win32]::SetProcessDPIAware()

$argStr = (@($ArgList) | Where-Object { $_ -ne $null -and $_ -ne '' }) -join ' '
if ($argStr) {
    $p = Start-Process -FilePath $Exe -ArgumentList $argStr -PassThru
} else {
    $p = Start-Process -FilePath $Exe -PassThru
}

for ($i = 0; $i -lt 80; $i++) {
    Start-Sleep -Milliseconds 150
    $p.Refresh()
    if ($p.HasExited) { throw "process exited with $($p.ExitCode)" }
    if ($p.MainWindowHandle -ne 0) { break }
}
Start-Sleep -Milliseconds $WaitMs
$p.Refresh()
$h = $p.MainWindowHandle
if ($h -eq 0) { throw 'no main window' }

$rect = New-Object RECT
[void][Win32]::GetWindowRect($h, [ref]$rect)

if (-not $PrintWindow) {
    # 置顶 + 置前，再按屏幕真实像素抓取
    [void][Win32]::SetWindowPos($h, [IntPtr](-1), $rect.Left, $rect.Top, 0, 0, 0x0001 -bor 0x0002 -bor 0x0010)
    [void][Win32]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 800
    $rect = New-Object RECT
    [void][Win32]::GetWindowRect($h, [ref]$rect)
}

$w = $rect.Right - $rect.Left
$ht = $rect.Bottom - $rect.Top
$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)

if ($PrintWindow) {
    $hdc = $g.GetHdc()
    $ok = [Win32]::PrintWindow($h, $hdc, 2)
    $g.ReleaseHdc($hdc)
} else {
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
    $ok = $true
}

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Stop-Process -Id $p.Id -Force
Write-Host ("saved {0} ({1}x{2}) printwindow={3}" -f $Out, $w, $ht, $ok)
