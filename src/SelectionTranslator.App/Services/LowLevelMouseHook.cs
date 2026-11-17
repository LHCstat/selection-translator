using System.Runtime.InteropServices;

namespace SelectionTranslator.App.Services;

public sealed record MouseHookEventArgs(int X, int Y, long Time);

public sealed class LowLevelMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;

    private readonly HookProc _proc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private bool _running;

    public LowLevelMouseHook() => _proc = HookCallback;

    public event EventHandler<MouseHookEventArgs>? MouseDown;
    public event EventHandler<MouseHookEventArgs>? MouseUp;
    public event EventHandler<MouseHookEventArgs>? Wheel;

    public bool Start()
    {
        if (_running)
            return true;

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);

            if (_hook == IntPtr.Zero)
            {
                started.TrySetResult(false);
                return;
            }

            _running = true;
            started.TrySetResult(true);

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _running = false;
        })
        {
            IsBackground = true,
            Name = "LowLevelMouseHook"
        };

        _thread.Start();
        return started.Task.GetAwaiter().GetResult();
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var args = new MouseHookEventArgs(data.pt.X, data.pt.Y, data.time);

                switch (wParam.ToInt32())
                {
                    case WmLButtonDown: MouseDown?.Invoke(this, args); break;
                    case WmLButtonUp: MouseUp?.Invoke(this, args); break;
                    case WmMouseWheel: Wheel?.Invoke(this, args); break;
                }
            }
            catch (Exception)
            {
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Stop()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose() => Stop();

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetMessageW", CharSet = CharSet.Unicode)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW", CharSet = CharSet.Unicode)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
}