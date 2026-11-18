using System.Windows;
using System.Windows.Input;
using SelectionTranslator.App.Services;
using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.App.Views;

public partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, (string BaseUrl, string Model)> Presets = new()
    {
        ["glm"] = ("https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
        ["siliconflow"] = ("https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct"),
        ["deepseek"] = ("https://api.deepseek.com/v1", "deepseek-chat"),
        ["ollama"] = ("http://127.0.0.1:11434/v1", "qwen2.5:7b")
    };

    private readonly SettingsStore _store;
    private readonly ISecretProtector _protector;
    private readonly Action _onSaved;
    private readonly Action _clearCache;
    private bool _loading = true;

    public SettingsWindow(
        SettingsStore store,
        ISecretProtector protector,
        Action onSaved,
        Action clearCache)
    {
        InitializeComponent();
        _store = store;
        _protector = protector;
        _onSaved = onSaved;
        _clearCache = clearCache;

        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 24);
        Height = Math.Min(784, MaxHeight);

        LoadSettings();
        _loading = false;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    private void LoadSettings()
    {
        var settings = _store.Current;

        HotkeyBox.Text = settings.Hotkey;
        ShowIconBox.IsChecked = settings.Trigger.ShowIcon;
        AutoTranslateBox.IsChecked = settings.Trigger.AutoTranslate;
        AutoStartBox.IsChecked = AutoStartService.IsEnabled();

        BaseUrlBox.Text = settings.Translate.BaseUrl;
        ModelBox.Text = settings.Translate.Model;
        TargetLangBox.Text = settings.Translate.TargetLang;
        IncludeTitleBox.IsChecked = settings.Translate.IncludeWindowTitle;
        CacheBox.IsChecked = settings.Translate.EnableCache;

        if (!string.IsNullOrEmpty(settings.Translate.ApiKeyProtected))
        {
            try { ApiKeyBox.Password = _protector.Unprotect(settings.Translate.ApiKeyProtected); }
            catch (Exception) { ApiKeyBox.Password = string.Empty; }
        }

        ExcludeBox.Text = string.Join(Environment.NewLine, settings.Exclude.Processes);

        SelectProvider(settings.Translate.Provider);
        SelectTheme(settings.Ui.Theme);
    }

    private void SelectProvider(string provider)
    {
        switch (provider)
        {
            case "siliconflow": ChipSilicon.IsChecked = true; break;
            case "deepseek": ChipDeepSeek.IsChecked = true; break;
            case "ollama": ChipOllama.IsChecked = true; break;
            case "glm": ChipGlm.IsChecked = true; break;
            default: ChipCustom.IsChecked = true; break;
        }
    }

    private void SelectTheme(string theme)
    {
        switch (theme)
        {
            case "light": ThemeLight.IsChecked = true; break;
            case "dark": ThemeDark.IsChecked = true; break;
            default: ThemeSystem.IsChecked = true; break;
        }
    }

    private void OnProviderChecked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        if (sender is System.Windows.Controls.RadioButton { Tag: string id } && Presets.TryGetValue(id, out var preset))
        {
            BaseUrlBox.Text = preset.BaseUrl;
            ModelBox.Text = preset.Model;
        }
    }

    private string SelectedProvider()
    {
        if (ChipSilicon.IsChecked == true) return "siliconflow";
        if (ChipDeepSeek.IsChecked == true) return "deepseek";
        if (ChipOllama.IsChecked == true) return "ollama";
        if (ChipCustom.IsChecked == true) return "custom";
        return "glm";
    }

    private string SelectedTheme()
    {
        if (ThemeLight.IsChecked == true) return "light";
        if (ThemeDark.IsChecked == true) return "dark";
        return "system";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var settings = _store.Current;
        settings.Hotkey = HotkeyBox.Text.Trim();
        settings.Trigger.ShowIcon = ShowIconBox.IsChecked == true;
        settings.Trigger.AutoTranslate = AutoTranslateBox.IsChecked == true;
        settings.Trigger.AutoStart = AutoStartBox.IsChecked == true;

        settings.Translate.Provider = SelectedProvider();
        settings.Translate.BaseUrl = BaseUrlBox.Text.Trim();
        settings.Translate.Model = ModelBox.Text.Trim();
        settings.Translate.TargetLang = TargetLangBox.Text.Trim();
        settings.Translate.IncludeWindowTitle = IncludeTitleBox.IsChecked == true;
        settings.Translate.EnableCache = CacheBox.IsChecked == true;
        settings.Translate.ApiKeyProtected = string.IsNullOrEmpty(ApiKeyBox.Password)
            ? string.Empty
            : _protector.Protect(ApiKeyBox.Password);

        settings.Ui.Theme = SelectedTheme();

        settings.Exclude.Processes = ExcludeBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _store.Save();
        _onSaved();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        _clearCache();

        if (sender is System.Windows.Controls.Button button)
            button.Content = "已清除";
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
}