using System.Text;
using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.Tests.Selection;

public class SseReaderTests
{
    private static async Task<List<string>> ReadAsync(string raw)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var result = new List<string>();
        await foreach (var item in SseReader.ReadDataEventsAsync(stream, CancellationToken.None))
            result.Add(item);
        return result;
    }

    [Fact]
    public async Task Reads_single_data_line()
    {
        var events = await ReadAsync("data: hello\n\n");
        Assert.Equal(new[] { "hello" }, events);
    }

    [Fact]
    public async Task Skips_comments_and_ignores_non_data_fields()
    {
        var events = await ReadAsync(": ping\nevent: message\ndata: hi\n\n");
        Assert.Equal(new[] { "hi" }, events);
    }

    [Fact]
    public async Task Joins_multiline_data()
    {
        var events = await ReadAsync("data: a\ndata: b\n\n");
        Assert.Equal(new[] { "a\nb" }, events);
    }

    [Fact]
    public async Task Flushes_last_event_without_trailing_blank_line()
    {
        var events = await ReadAsync("data: tail");
        Assert.Equal(new[] { "tail" }, events);
    }

    [Fact]
    public async Task Extracts_openai_deltas_and_stops_at_done()
    {
        const string raw =
            "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
            "data: [DONE]\n\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var deltas = new List<string>();
        await foreach (var d in OpenAiStream.ReadDeltasAsync(stream, CancellationToken.None))
            deltas.Add(d);
        Assert.Equal(new[] { "你", "好" }, deltas);
    }

    [Fact]
    public async Task Skips_malformed_json()
    {
        const string raw = "data: {bad json}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\n";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        var deltas = new List<string>();
        await foreach (var d in OpenAiStream.ReadDeltasAsync(stream, CancellationToken.None))
            deltas.Add(d);
        Assert.Equal(new[] { "ok" }, deltas);
    }
}