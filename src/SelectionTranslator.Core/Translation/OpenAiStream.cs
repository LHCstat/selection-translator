using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SelectionTranslator.Core.Translation;

public static class OpenAiStream
{
    public static async IAsyncEnumerable<string> ReadDeltasAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var payload in SseReader.ReadDataEventsAsync(stream, cancellationToken))
        {
            if (payload == "[DONE]")
                yield break;

            string? delta = null;

            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var m)
                        ? m.GetString()
                        : null;
                    throw new TranslationException(message ?? "翻译服务返回错误");
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];

                if (choice.TryGetProperty("delta", out var deltaElement) &&
                    deltaElement.TryGetProperty("content", out var content))
                {
                    delta = content.GetString();
                }
                else if (choice.TryGetProperty("text", out var text))
                {
                    delta = text.GetString();
                }
            }
            catch (JsonException)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(delta))
                yield return delta;
        }
    }
}