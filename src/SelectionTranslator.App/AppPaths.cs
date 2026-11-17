using System.IO;

namespace SelectionTranslator.App;

public sealed class AppPaths
{
    public AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SelectionTranslator");
        LogDirectory = Path.Combine(Root, "logs");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
    }

    public string Root { get; }
    public string LogDirectory { get; }

    public string ConfigFile => Path.Combine(Root, "config.json");
    public string CacheFile => Path.Combine(Root, "cache.json");
}