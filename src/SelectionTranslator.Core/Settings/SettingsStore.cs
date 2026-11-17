using System.Text.Json;

namespace SelectionTranslator.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Current = new AppSettings();
            Save();
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options)
                      ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
        File.Move(temp, _path, overwrite: true);
    }
}