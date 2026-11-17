Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class WinList {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$target = (Get-Process SelectionTranslator.App -ErrorAction Stop).Id
$script:lines = @()

$callback = [WinList+EnumProc]{
    param($handle, $state)

    $pid2 = 0
    [WinList]::GetWindowThreadProcessId($handle, [ref]$pid2) | Out-Null
    if ($pid2 -ne $target) { return $true }

    $rect = New-Object WinList+RECT
    [WinList]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $title = New-Object System.Text.StringBuilder 256
    [WinList]::GetWindowText($handle, $title, 256) | Out-Null
    $class = New-Object System.Text.StringBuilder 256
    [WinList]::GetClassName($handle, $class, 256) | Out-Null
    $visible = [WinList]::IsWindowVisible($handle)

    $script:lines += ("handle={0} visible={1} rect={2},{3},{4},{5} class={6} title={7}" -f `
        $handle, $visible, $rect.Left, $rect.Top, $rect.Right, $rect.Bottom, $class.ToString(), $title.ToString())

    return $true
}

[WinList]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
$script:lines | ForEach-Object { Write-Host $_ }