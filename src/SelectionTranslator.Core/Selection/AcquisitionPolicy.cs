namespace SelectionTranslator.Core.Selection;

public interface IAcquisitionPolicy
{
    bool IsAllowed(string acquirerName, SelectionContext context);
}

public sealed class DefaultAcquisitionPolicy : IAcquisitionPolicy
{
    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "wt", "conhost", "cmd", "powershell", "pwsh"
    };

    private readonly Func<IReadOnlyCollection<string>> _excludedProvider;

    public DefaultAcquisitionPolicy(IReadOnlyCollection<string> excludedProcesses)
        : this(() => excludedProcesses)
    {
    }

    public DefaultAcquisitionPolicy(Func<IReadOnlyCollection<string>> excludedProcesses)
    {
        _excludedProvider = excludedProcesses;
    }

    public bool IsAllowed(string acquirerName, SelectionContext context)
    {
        var process = context.ProcessName;
        if (string.IsNullOrEmpty(process))
            return true;

        if (_excludedProvider().Any(p => string.Equals(p, process, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (acquirerName == "clipboard" && TerminalProcesses.Contains(process))
            return false;

        return true;
    }
}