namespace SelectionTranslator.Core.Settings;

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Alt+D";
    public TriggerSettings Trigger { get; set; } = new();
    public TranslateSettings Translate { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public ExcludeSettings Exclude { get; set; } = new();
}

public sealed class TriggerSettings
{
    public bool ShowIcon { get; set; } = true;
    public bool AutoTranslate { get; set; }
    public bool AutoStart { get; set; }
}

public sealed class TranslateSettings
{
    public string Provider { get; set; } = "glm";
    public string Model { get; set; } = "glm-4-flash";
    public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4";
    public string ApiKeyProtected { get; set; } = string.Empty;
    public string TargetLang { get; set; } = "简体中文";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class UiSettings
{
    public string Theme { get; set; } = "system";
    public double FontSize { get; set; } = 14;
    public double PopupWidth { get; set; } = 420;
}

public sealed class ExcludeSettings
{
    public List<string> Processes { get; set; } = new();
}