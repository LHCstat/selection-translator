using System.Net.Http;
using SelectionTranslator.Core.Settings;

namespace SelectionTranslator.Core.Translation;

public static class ProviderFactory
{
    public static ITranslationProvider Create(
        TranslateSettings settings,
        ISecretProtector protector,
        HttpClient http)
    {
        var apiKey = string.IsNullOrEmpty(settings.ApiKeyProtected)
            ? null
            : protector.Unprotect(settings.ApiKeyProtected);

        var options = new ProviderOptions(
            settings.BaseUrl,
            settings.Model,
            apiKey,
            TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var displayName = settings.Provider switch
        {
            "glm" => "智谱 GLM",
            "ollama" => "Ollama",
            _ => settings.Provider
        };

        return new OpenAiCompatibleProvider(settings.Provider, displayName, http, options);
    }
}