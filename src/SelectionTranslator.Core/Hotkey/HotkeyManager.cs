using System.Windows.Interop;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Hotkey;

public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;

    private HwndSource? _source;

    public event EventHandler? Pressed;

    public bool Register(HotkeyGesture hotkey)
    {
        Unregister();

        var parameters = new HwndSourceParameters("SelectionTranslatorHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = new IntPtr(-3)
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        return NativeMethods.RegisterHotKey(
            _source.Handle, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_source is null)
            return;

        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    public void Dispose() => Unregister();
}