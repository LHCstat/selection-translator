using SelectionTranslator.Core.Selection;

namespace SelectionTranslator.Tests.Selection;

public class SelectionPipelineTests
{
    private sealed class FakeAcquirer : ITextAcquirer
    {
        private readonly string? _text;
        public FakeAcquirer(string name, string? text) { Name = name; _text = text; }
        public string Name { get; }
        public bool Called { get; private set; }

        public Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(_text);
        }
    }

    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Returns_first_valid_result()
    {
        var uia = new FakeAcquirer("uia", "hello");
        var clipboard = new FakeAcquirer("clipboard", "world");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia, clipboard },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("hello", result!.Text);
        Assert.False(clipboard.Called);
    }

    [Fact]
    public async Task Falls_back_when_first_returns_invalid_text()
    {
        var uia = new FakeAcquirer("uia", "   ");
        var clipboard = new FakeAcquirer("clipboard", "world");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia, clipboard },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None);

        Assert.Equal("world", result!.Text);
        Assert.Equal("clipboard", result.AcquirerName);
    }

    [Fact]
    public async Task Skips_clipboard_in_terminal()
    {
        var uia = new FakeAcquirer("uia", null);
        var clipboard = new FakeAcquirer("clipboard", "danger");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia, clipboard },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "WindowsTerminal", 0), CancellationToken.None);

        Assert.Null(result);
        Assert.False(clipboard.Called);
    }

    [Fact]
    public async Task Skips_excluded_process()
    {
        var uia = new FakeAcquirer("uia", "secret");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { uia },
            new DefaultAcquisitionPolicy(new[] { "KeePass" }));

        var result = await pipeline.AcquireAsync(new SelectionContext(0, 0, "KeePass", 0), CancellationToken.None);

        Assert.Null(result);
        Assert.False(uia.Called);
    }

    [Fact]
    public void Exclusion_list_is_read_live()
    {
        var excluded = new List<string>();
        var policy = new DefaultAcquisitionPolicy(() => excluded);
        var context = new SelectionContext(0, 0, "KeePass", 0);

        Assert.True(policy.IsAllowed("uia", context));
        excluded.Add("KeePass");
        Assert.False(policy.IsAllowed("uia", context));
    }

    [Fact]
    public async Task Suppresses_identical_text_within_800ms()
    {
        var time = new FakeTime();
        var acquirer = new FakeAcquirer("uia", "hello");
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { acquirer },
            new DefaultAcquisitionPolicy(Array.Empty<string>()),
            time);

        Assert.NotNull(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));
        Assert.Null(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));

        time.Now = time.Now.AddSeconds(1);
        Assert.NotNull(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));
    }

    [Fact]
    public async Task Returns_null_when_all_acquirers_fail()
    {
        var pipeline = new SelectionPipeline(
            new ITextAcquirer[] { new FakeAcquirer("uia", null), new FakeAcquirer("clipboard", null) },
            new DefaultAcquisitionPolicy(Array.Empty<string>()));

        Assert.Null(await pipeline.AcquireAsync(new SelectionContext(0, 0, "chrome", 0), CancellationToken.None));
    }
}