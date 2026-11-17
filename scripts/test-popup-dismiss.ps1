param([string]$Configuration = "Debug")

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\SelectionTranslator.App\bin\$Configuration\net8.0-windows\SelectionTranslator.App.exe"

Stop-Process -Name SelectionTranslator.App -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Start-Process -FilePath $exe -ArgumentList "--preview"
Start-Sleep -Seconds 5

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Clicker {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@

[Clicker]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$saved = New-Object Clicker+POINT
[Clicker]::GetCursorPos([ref]$saved) | Out-Null

$target = (Get-Process SelectionTranslator.App -ErrorAction Stop).Id
$script:popup = [IntPtr]::Zero
$script:rect = New-Object Clicker+RECT

$callback = [Clicker+EnumProc]{
    param($handle, $state)

    $processId = 0
    [Clicker]::GetWindowThreadProcessId($handle, [ref]$processId) | Out-Null
    if ($processId -ne $target -or -not [Clicker]::IsWindowVisible($handle)) { return $true }

    $rect = New-Object Clicker+RECT
    [Clicker]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    if ($width -gt 300 -and $height -gt 80) {
        $script:popup = $handle
        $script:rect = $rect
    }

    return $true
}

[Clicker]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

if ($script:popup -eq [IntPtr]::Zero) {
    throw "Popup window not found."
}

function Click-At([int]$x, [int]$y) {
    [Clicker]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [Clicker]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [Clicker]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 600
}

$centerX = [int](($script:rect.Left + $script:rect.Right) / 2)
$centerY = [int](($script:rect.Top + $script:rect.Bottom) / 2)

Click-At $centerX ($centerY - 30)
$visibleAfterInside = [Clicker]::IsWindowVisible($script:popup)

Click-At 20 20
$visibleAfterOutside = [Clicker]::IsWindowVisible($script:popup)

[Clicker]::SetCursorPos($saved.X, $saved.Y) | Out-Null

Write-Host "visible after inside click: $visibleAfterInside"
Write-Host "visible after outside click: $visibleAfterOutside"