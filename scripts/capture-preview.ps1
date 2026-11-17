param([string]$Configuration = "Debug")

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\SelectionTranslator.App\bin\$Configuration\net8.0-windows\SelectionTranslator.App.exe"
$out = Join-Path $root "shot-popup.png"

Stop-Process -Name SelectionTranslator.App -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Start-Process -FilePath $exe -ArgumentList "--preview"
Start-Sleep -Seconds 5

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cap2 {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
"@

[Cap2]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$x = 300
$y = 240
$width = 780
$height = 560

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($x, $y, 0, 0, $bitmap.Size)
$bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "saved $out ($width x $height)"