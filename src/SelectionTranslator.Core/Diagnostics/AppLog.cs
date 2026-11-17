namespace SelectionTranslator.Core.Diagnostics;

public static class AppLog
{
    private static readonly object Sync = new();
    private static string? _directory;

    public static void Init(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private static void Write(string level, string message)
    {
        if (_directory is null)
            return;

        lock (Sync)
        {
            try
            {
                var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }
    }
}