namespace SelectionTranslator.Core.Selection;

public static class TextValidator
{
    public const int MaxLength = 5000;

    public static bool TryNormalize(string? raw, out string text, out string? reason)
    {
        text = (raw ?? string.Empty).Trim();
        reason = null;

        if (text.Length == 0)
        {
            reason = "empty";
            return false;
        }

        if (text.Length > MaxLength)
        {
            reason = "too-long";
            return false;
        }

        if (!text.Any(char.IsLetter))
        {
            reason = "no-letter";
            return false;
        }

        return true;
    }
}