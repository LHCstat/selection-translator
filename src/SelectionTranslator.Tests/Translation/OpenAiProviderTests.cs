using System.Net;
using System.Text;
using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.Tests.Translation;

public class OpenAiProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }

    private static OpenAiCompatibleProvider CreateProvider(HttpStatusCode status, string body) =>
        new("test", "Test", new HttpClient(new StubHandler(status, body)),
            new ProviderOptions("http://localhost/v1", "test-model", "key"));

    [Fact]
    public async Task Streams_deltas_and_reports_final()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var provider = CreateProvider(HttpStatusCode.OK, sse);
        var chunks = new List<TranslationChunk>();

        await provider.TranslateAsync(
            new TranslationRequest("hello", null, "简体中文"),
            new SyncProgress<TranslationChunk>(chunks.Add),
            CancellationToken.None);

        Assert.Equal("你好", string.Concat(chunks.Where(c => !c.IsFinal).Select(c => c.Delta)));
        Assert.Contains(chunks, c => c.IsFinal);
    }

    [Fact]
    public async Task Throws_on_401()
    {
        var provider = CreateProvider(HttpStatusCode.Unauthorized, "{\"error\":\"bad key\"}");

        var ex = await Assert.ThrowsAsync<TranslationException>(() =>
            provider.TranslateAsync(
                new TranslationRequest("hello", null, "简体中文"),
                new SyncProgress<TranslationChunk>(_ => { }),
                CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Throws_when_result_is_empty()
    {
        var provider = CreateProvider(HttpStatusCode.OK, "data: [DONE]\n\n");

        await Assert.ThrowsAsync<TranslationException>(() =>
            provider.TranslateAsync(
                new TranslationRequest("hello", null, "简体中文"),
                new SyncProgress<TranslationChunk>(_ => { }),
                CancellationToken.None));
    }
}