param([string]$Configuration = "Debug")

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\SelectionTranslator.App\bin\$Configuration\net8.0-windows\SelectionTranslator.App.exe"
$out = Join-Path $root "shot-settings.png"

Stop-Process -Name SelectionTranslator.App -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Start-Process -FilePath $exe -ArgumentList "--settings"
Start-Sleep -Seconds 3

$process = Get-Process SelectionTranslator.App -ErrorAction Stop
$handle = $process.MainWindowHandle
if ($handle -eq 0) {
    throw "Settings window did not open."
}

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cap {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

[Cap]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
[Cap]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object Cap+RECT
[Cap]::GetWindowRect([IntPtr]$handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "saved $out ($width x $height)"