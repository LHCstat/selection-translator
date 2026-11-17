using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.App.Services;

public static class WindowPlacer
{
    private const uint MonitorDefaultToNearest = 2;

    public static void MoveTo(Window window, int screenX, int screenY)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            screenX,
            screenY,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    public static void PlaceNear(Window window, int screenX, int screenY)
    {
        var handle = new WindowInteropHelper(window).Handle;

        var width = 420;
        var height = 300;
        if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out var windowRect))
        {
            width = windowRect.Right - windowRect.Left;
            height = windowRect.Bottom - windowRect.Top;
        }

        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        var monitor = NativeMethods.MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        int left, top, right, bottom;

        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            left = info.rcWork.Left;
            top = info.rcWork.Top;
            right = info.rcWork.Right;
            bottom = info.rcWork.Bottom;
        }
        else
        {
            left = 0;
            top = 0;
            right = (int)SystemParameters.PrimaryScreenWidth;
            bottom = (int)SystemParameters.PrimaryScreenHeight;
        }

        var targetLeft = Math.Min(screenX + 16, right - width - 8);
        var targetTop = Math.Min(screenY + 16, bottom - height - 8);

        MoveTo(window, Math.Max(targetLeft, left + 8), Math.Max(targetTop, top + 8));
    }
}