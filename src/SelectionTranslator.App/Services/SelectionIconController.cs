using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SelectionTranslator.App.Views;
using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;
using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.App.Services;

public sealed class SelectionIconController : IDisposable
{
    private readonly LowLevelMouseHook _hook;
    private readonly DragTracker _tracker = new();
    private readonly SettingsStore _settings;
    private readonly TranslationCoordinator _coordinator;
    private readonly DispatcherTimer _hideTimer;

    private SelectionIconWindow? _icon;
    private string? _processName;
    private int _x;
    private int _y;

    public SelectionIconController(
        LowLevelMouseHook hook,
        SettingsStore settings,
        TranslationCoordinator coordinator)
    {
        _hook = hook;
        _settings = settings;
        _coordinator = coordinator;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) => HideIcon();

        _hook.MouseDown += OnMouseDown;
        _hook.MouseUp += OnMouseUp;
        _hook.Wheel += (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(HideIcon));
    }

    public void Start()
    {
        if (!_hook.Start())
            throw new InvalidOperationException("全局鼠标钩子启动失败");
    }

    private void OnMouseDown(object? sender, MouseHookEventArgs e)
    {
        _tracker.OnDown(e.X, e.Y, e.Time);

        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            _coordinator.DismissIfOutside(e.X, e.Y)));

        if (_icon?.IsVisible != true || IsInsideIcon(e.X, e.Y))
            return;

        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(HideIcon));
    }

    private void OnMouseUp(object? sender, MouseHookEventArgs e)
    {
        if (!_tracker.OnUp(e.X, e.Y, e.Time))
            return;

        _x = e.X;
        _y = e.Y;
        _processName = GetProcessName();

        if (_settings.Current.Trigger.AutoTranslate)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                _ = _coordinator.TranslateSelectionAsync(_x, _y, _processName)));
            return;
        }

        if (_settings.Current.Trigger.ShowIcon)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ShowIcon));
    }

    private void ShowIcon()
    {
        HideIcon();

        _icon = new SelectionIconWindow();
        _icon.Clicked += async (_, _) =>
            await _coordinator.TranslateSelectionAsync(_x, _y, _processName);

        _icon.Show();
        _icon.MoveTo(_x + 12, _y + 12);
        _icon.Dispatcher.BeginInvoke(new Action(() =>
        {
            _icon?.MoveTo(_x + 12, _y + 12);
        }), DispatcherPriority.ApplicationIdle);
        _hideTimer.Start();
    }

    private void HideIcon()
    {
        _hideTimer.Stop();

        if (_icon is null)
            return;

        _icon.Close();
        _icon = null;
    }

    private bool IsInsideIcon(int x, int y)
    {
        if (_icon is null)
            return false;

        var handle = new WindowInteropHelper(_icon).Handle;
        if (!NativeMethods.GetWindowRect(handle, out var rect))
            return false;

        return x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom;
    }

    private static string? GetProcessName()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _hideTimer.Stop();
        _hook.Dispose();
    }
}