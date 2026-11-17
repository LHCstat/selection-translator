using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SelectionTranslator.Core.Translation;

public sealed record ProviderOptions(
    string BaseUrl,
    string Model,
    string? ApiKey,
    TimeSpan? Timeout = null);

public sealed class OpenAiCompatibleProvider : ITranslationProvider
{
    private readonly HttpClient _http;
    private readonly ProviderOptions _options;

    public OpenAiCompatibleProvider(
        string id,
        string displayName,
        HttpClient http,
        ProviderOptions options)
    {
        Id = id;
        DisplayName = displayName;
        _http = http;
        _options = options;
    }

    public string Id { get; }
    public string DisplayName { get; }

    public async Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationChunk> onChunk,
        CancellationToken cancellationToken)
    {
        var messages = PromptBuilder.Build(request);

        var payload = new
        {
            model = _options.Model,
            stream = true,
            temperature = 0.2,
            messages = messages.Select(m => new { role = m.Role, content = m.Content })
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUrl());
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        if (!string.IsNullOrEmpty(_options.ApiKey))
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout ?? TimeSpan.FromSeconds(60));

        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            throw new TranslationException(
                MapError(response.StatusCode, body),
                (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);

        var builder = new StringBuilder();

        await foreach (var delta in OpenAiStream.ReadDeltasAsync(stream, timeout.Token))
        {
            builder.Append(delta);
            onChunk.Report(new TranslationChunk(delta, false));
        }

        if (builder.Length == 0)
            throw new TranslationException("翻译结果为空");

        onChunk.Report(new TranslationChunk(string.Empty, true));
    }

    private string BuildUrl()
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/chat/completions";
    }

    private static string MapError(HttpStatusCode status, string body)
    {
        var detail = body.Length > 300 ? body[..300] : body;
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"API Key 无效或无权限（{(int)status}）",
            HttpStatusCode.TooManyRequests =>
                "请求过于频繁，请稍后再试（429）",
            _ => $"翻译服务返回 {(int)status}：{detail}"
        };
    }
}