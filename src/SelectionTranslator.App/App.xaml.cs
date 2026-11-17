using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SelectionTranslator.App.Services;
using SelectionTranslator.App.Views;
using SelectionTranslator.Core.Diagnostics;
using SelectionTranslator.Core.Hotkey;
using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;
using SelectionTranslator.Core.Settings;
using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.App;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private ServiceProvider? _services;
    private TrayIconService? _tray;
    private HotkeyManager? _hotkeyManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\SelectionTranslator.SingleInstance", out var createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("unhandled dispatcher exception", args.Exception);
            System.Windows.MessageBox.Show("划译发生错误，详情已写入日志。", "划译", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        var paths = new AppPaths();
        AppLog.Init(paths.LogDirectory);

        var services = new ServiceCollection();
        services.AddSingleton(paths);
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IInputSimulator, InputSimulator>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton(_ => new SettingsStore(paths.ConfigFile));
        services.AddSingleton(_ => new TranslationCache(paths.CacheFile));
        services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<SelectionPipeline>(sp =>
        {
            var settingsStore = sp.GetRequiredService<SettingsStore>();
            var acquirers = new ITextAcquirer[]
            {
                new UiaTextAcquirer(),
                new ClipboardTextAcquirer(
                    sp.GetRequiredService<IClipboardService>(),
                    sp.GetRequiredService<IInputSimulator>())
            };

            return new SelectionPipeline(
                acquirers,
                new DefaultAcquisitionPolicy(() => settingsStore.Current.Exclude.Processes));
        });
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<TranslationCoordinator>();
        services.AddSingleton<HotkeyManager>();
        services.AddSingleton<LowLevelMouseHook>();
        services.AddSingleton<SelectionIconController>();
        _services = services.BuildServiceProvider();

        var settingsStore = _services.GetRequiredService<SettingsStore>();
        settingsStore.Load();
        _services.GetRequiredService<TranslationCache>().Load();

        _tray = _services.GetRequiredService<TrayIconService>();
        _tray.Show();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.SettingsRequested += (_, _) => ShowSettings();

        _hotkeyManager = _services.GetRequiredService<HotkeyManager>();
        _hotkeyManager.Pressed += async (_, _) =>
            await _services.GetRequiredService<TranslationCoordinator>().TranslateCurrentSelectionAsync();
        ApplyHotkey(settingsStore.Current.Hotkey);

        try
        {
            _services.GetRequiredService<SelectionIconController>().Start();
        }
        catch (Exception ex)
        {
            AppLog.Error("mouse hook failed", ex);
            _tray.Notify("划词功能启动失败，快捷键仍可使用");
        }

        AppLog.Info("started");

        if (string.IsNullOrEmpty(settingsStore.Current.Translate.ApiKeyProtected))
            Dispatcher.BeginInvoke(new Action(ShowSettings));
    }

    private void ShowSettings()
    {
        var window = new SettingsWindow(
            _services!.GetRequiredService<SettingsStore>(),
            _services!.GetRequiredService<ISecretProtector>(),
            ApplySettings);
        window.ShowDialog();
    }

    private void ApplySettings()
    {
        var settings = _services!.GetRequiredService<SettingsStore>().Current;
        AutoStartService.SetEnabled(settings.Trigger.AutoStart);
        ApplyHotkey(settings.Hotkey);
    }

    private void ApplyHotkey(string text)
    {
        if (HotkeyParser.TryParse(text, out var hotkey) && _hotkeyManager!.Register(hotkey))
            return;

        _tray?.Notify("快捷键注册失败，请换一个组合键");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();

        if (_ownsMutex)
            _mutex?.ReleaseMutex();

        _mutex?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}