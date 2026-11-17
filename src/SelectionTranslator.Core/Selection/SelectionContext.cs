namespace SelectionTranslator.Core.Selection;

public sealed record SelectionContext(int X, int Y, string? ProcessName, nint Hwnd);

public sealed record SelectionResult(string Text, string AcquirerName);