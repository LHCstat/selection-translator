Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class ScreenCap {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
}
"@

[ScreenCap]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$width = [ScreenCap]::GetSystemMetrics(0)
$height = [ScreenCap]::GetSystemMetrics(1)

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "shot-screen.png"

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen(0, 0, 0, 0, $bitmap.Size)
$bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "saved $out ($width x $height)"