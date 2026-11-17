using System.Diagnostics;
using System.Windows;
using SelectionTranslator.App.ViewModels;
using SelectionTranslator.App.Views;
using SelectionTranslator.Core.Diagnostics;
using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;
using SelectionTranslator.Core.Settings;
using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.App.Services;

public sealed class TranslationCoordinator
{
    private readonly SelectionPipeline _pipeline;
    private readonly SettingsStore _settings;
    private readonly ISecretProtector _protector;
    private readonly HttpClient _http;
    private readonly TranslationCache _cache;
    private readonly TrayIconService _toast;

    private CancellationTokenSource? _cts;
    private TranslationPopupWindow? _popup;
    private string? _lastText;
    private int _lastX;
    private int _lastY;

    public TranslationCoordinator(
        SelectionPipeline pipeline,
        SettingsStore settings,
        ISecretProtector protector,
        HttpClient http,
        TranslationCache cache,
        TrayIconService toast)
    {
        _pipeline = pipeline;
        _settings = settings;
        _protector = protector;
        _http = http;
        _cache = cache;
        _toast = toast;
    }

    public async Task TranslateSelectionAsync(int x, int y, string? processName)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var cancellationToken = _cts.Token;

        SelectionResult? result;

        try
        {
            result = await _pipeline.AcquireAsync(
                new SelectionContext(x, y, processName, IntPtr.Zero),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (result is null)
        {
            _toast.Notify("未取到选中文字");
            return;
        }

        await TranslateTextAsync(result.Text, x, y, cancellationToken);
    }

    public async Task TranslateCurrentSelectionAsync()
    {
        NativeMethods.GetCursorPos(out var point);
        await TranslateSelectionAsync(point.X, point.Y, ForegroundProcessName());
    }

    public async Task TranslateTextAsync(string text, int x, int y, CancellationToken cancellationToken = default)
    {
        _lastText = text;
        _lastX = x;
        _lastY = y;

        var viewModel = new TranslationPopupViewModel
        {
            OriginalText = text,
            Translation = string.Empty,
            Error = null,
            IsStreaming = true,
            Status = "翻译中…"
        };

        viewModel.RetryRequested += (_, _) => _ = RetryAsync();
        viewModel.CloseRequested += (_, _) => _popup?.Hide();

        if (_popup is null)
        {
            _popup = new TranslationPopupWindow();
            _popup.Closed += (_, _) => _popup = null;
        }

        _popup.ShowFor(viewModel, x, y, _settings.Current.Ui.PopupWidth);

        var settings = _settings.Current.Translate;
        var provider = ProviderFactory.Create(settings, _protector, _http);
        var key = CacheKey.For(provider.Id, settings.Model, settings.TargetLang, text);

        if (_cache.TryGet(key, out var cached))
        {
            viewModel.Translation = cached;
            viewModel.IsStreaming = false;
            viewModel.Status = "缓存";
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var firstChunk = true;

        var progress = new Progress<TranslationChunk>(chunk =>
        {
            if (chunk.IsFinal)
            {
                viewModel.IsStreaming = false;
                viewModel.Status = $"{stopwatch.ElapsedMilliseconds} ms";
                _cache.Set(key, viewModel.Translation);
                return;
            }

            viewModel.Translation += chunk.Delta;

            if (firstChunk)
            {
                firstChunk = false;
                viewModel.Status = $"首字 {stopwatch.ElapsedMilliseconds} ms";
            }
        });

        try
        {
            await provider.TranslateAsync(
                new TranslationRequest(text, null, settings.TargetLang, ForegroundWindowTitle()),
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            viewModel.IsStreaming = false;
        }
        catch (TranslationException ex)
        {
            viewModel.IsStreaming = false;
            viewModel.Error = ex.Message;
            viewModel.Status = "失败";
            AppLog.Error("translation failed", ex);
        }
        catch (Exception ex)
        {
            viewModel.IsStreaming = false;
            viewModel.Error = "网络错误，请检查连接后重试";
            viewModel.Status = "失败";
            AppLog.Error("translation failed", ex);
        }
    }

    private async Task RetryAsync()
    {
        if (_lastText is null)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        await TranslateTextAsync(_lastText, _lastX, _lastY, _cts.Token);
    }

    private static string? ForegroundProcessName()
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

    private static string? ForegroundWindowTitle()
    {
        try
        {
            var handle = NativeMethods.GetForegroundWindow();
            var buffer = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(handle, buffer, buffer.Capacity);
            var title = buffer.ToString().Trim();
            return title.Length == 0 ? null : title;
        }
        catch (Exception)
        {
            return null;
        }
    }
}