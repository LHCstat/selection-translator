using System.Security.Cryptography;
using System.Text;

namespace SelectionTranslator.Core.Translation;

public static class CacheKey
{
    public static string For(string providerId, string model, string targetLang, string text)
    {
        var raw = $"{providerId}\u0000{model}\u0000{targetLang}\u0000{text}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}