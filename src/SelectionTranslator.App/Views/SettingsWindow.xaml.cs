using System.Windows;
using System.Windows.Controls;
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

    public SettingsWindow(SettingsStore store, ISecretProtector protector, Action onSaved)
    {
        InitializeComponent();
        _store = store;
        _protector = protector;
        _onSaved = onSaved;

        LoadSettings();
        ProviderBox.SelectionChanged += OnProviderChanged;
    }

    private void LoadSettings()
    {
        var settings = _store.Current;

        HotkeyBox.Text = settings.Hotkey;
        ShowIconBox.IsChecked = settings.Trigger.ShowIcon;
        AutoTranslateBox.IsChecked = settings.Trigger.AutoTranslate;
        AutoStartBox.IsChecked = AutoStartService.IsEnabled();

        foreach (var item in ProviderBox.Items.OfType<ComboBoxItem>())
            if (Equals(item.Tag, settings.Translate.Provider))
                ProviderBox.SelectedItem = item;

        if (ProviderBox.SelectedItem is null)
            ProviderBox.SelectedIndex = 0;

        BaseUrlBox.Text = settings.Translate.BaseUrl;
        ModelBox.Text = settings.Translate.Model;
        TargetLangBox.Text = settings.Translate.TargetLang;

        if (!string.IsNullOrEmpty(settings.Translate.ApiKeyProtected))
        {
            try { ApiKeyBox.Password = _protector.Unprotect(settings.Translate.ApiKeyProtected); }
            catch (Exception) { ApiKeyBox.Password = string.Empty; }
        }

        ExcludeBox.Text = string.Join(Environment.NewLine, settings.Exclude.Processes);
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderBox.SelectedItem is not ComboBoxItem item)
            return;

        if (item.Tag is string id && Presets.TryGetValue(id, out var preset))
        {
            BaseUrlBox.Text = preset.BaseUrl;
            ModelBox.Text = preset.Model;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var settings = _store.Current;
        settings.Hotkey = HotkeyBox.Text.Trim();
        settings.Trigger.ShowIcon = ShowIconBox.IsChecked == true;
        settings.Trigger.AutoTranslate = AutoTranslateBox.IsChecked == true;
        settings.Trigger.AutoStart = AutoStartBox.IsChecked == true;

        if (ProviderBox.SelectedItem is ComboBoxItem item && item.Tag is string id)
            settings.Translate.Provider = id;

        settings.Translate.BaseUrl = BaseUrlBox.Text.Trim();
        settings.Translate.Model = ModelBox.Text.Trim();
        settings.Translate.TargetLang = TargetLangBox.Text.Trim();

        settings.Translate.ApiKeyProtected = string.IsNullOrEmpty(ApiKeyBox.Password)
            ? string.Empty
            : _protector.Protect(ApiKeyBox.Password);

        settings.Exclude.Processes = ExcludeBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _store.Save();
        _onSaved();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}