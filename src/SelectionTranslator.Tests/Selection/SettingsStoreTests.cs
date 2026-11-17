using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.Tests.Selection;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"config-{Guid.NewGuid():N}.json");

    [Fact]
    public void Creates_defaults_when_file_missing()
    {
        var store = new SettingsStore(_path);
        store.Load();
        Assert.Equal("Alt+D", store.Current.Hotkey);
        Assert.Equal("glm", store.Current.Translate.Provider);
        Assert.True(store.Current.Trigger.ShowIcon);
        Assert.False(store.Current.Trigger.AutoTranslate);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var store = new SettingsStore(_path);
        store.Load();
        store.Current.Hotkey = "Ctrl+Shift+T";
        store.Current.Translate.TargetLang = "English";
        store.Save();

        var reloaded = new SettingsStore(_path);
        reloaded.Load();
        Assert.Equal("Ctrl+Shift+T", reloaded.Current.Hotkey);
        Assert.Equal("English", reloaded.Current.Translate.TargetLang);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        File.WriteAllText(_path, "{ not json");
        var store = new SettingsStore(_path);
        store.Load();
        Assert.Equal("Alt+D", store.Current.Hotkey);
    }

    [Fact]
    public void Dpapi_roundtrip()
    {
        var protector = new DpapiSecretProtector();
        var encrypted = protector.Protect("sk-test-123");
        Assert.NotEqual("sk-test-123", encrypted);
        Assert.Equal("sk-test-123", protector.Unprotect(encrypted));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}