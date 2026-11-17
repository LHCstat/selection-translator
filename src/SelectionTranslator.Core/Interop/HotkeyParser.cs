namespace SelectionTranslator.Core.Interop;

public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey);

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static bool TryParse(string? text, out HotkeyGesture hotkey)
    {
        hotkey = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        uint modifiers = 0;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "alt": modifiers |= ModAlt; break;
                case "ctrl" or "control": modifiers |= ModControl; break;
                case "shift": modifiers |= ModShift; break;
                case "win" or "meta": modifiers |= ModWin; break;
                default: return false;
            }
        }

        var key = parts[^1].ToUpperInvariant();
        uint virtualKey;

        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
            virtualKey = key[0];
        else if (key.Length == 1 && key[0] is >= '0' and <= '9')
            virtualKey = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var function) && function is >= 1 and <= 24)
            virtualKey = (uint)(0x70 + function - 1);
        else if (key == "SPACE")
            virtualKey = 0x20;
        else
            return false;

        if (modifiers == 0)
            return false;

        hotkey = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }
}