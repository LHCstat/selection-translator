using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;

namespace SelectionTranslator.Tests.Selection;

public class ClipboardTextAcquirerTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public uint Sequence { get; set; } = 1;
        public string Text { get; set; } = string.Empty;
        public ClipboardSnapshot SnapshotResult { get; set; } = new(true, true, "previous");
        public bool Restored { get; private set; }
        public Action? OnRead { get; set; }

        public uint GetSequenceNumber() => Sequence;
        public bool TryGetText(out string text)
        {
            OnRead?.Invoke();
            text = Text;
            return !string.IsNullOrEmpty(Text);
        }

        public ClipboardSnapshot Snapshot() => SnapshotResult;
        public void Restore(object? snapshot) => Restored = true;
        public bool TrySetText(string text) { Text = text; return true; }
    }

    private sealed class FakeInput : IInputSimulator
    {
        private readonly FakeClipboard _clipboard;
        private readonly string? _text;

        public FakeInput(FakeClipboard clipboard, string? text = "selected text")
        {
            _clipboard = clipboard;
            _text = text;
        }

        public bool Called { get; private set; }

        public void SendCopy()
        {
            Called = true;
            _clipboard.Sequence++;
            if (_text is not null)
                _clipboard.Text = _text;
        }
    }

    private static ClipboardTextAcquirer Create(FakeClipboard clipboard, IInputSimulator input) =>
        new(clipboard, input, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(30));

    [Fact]
    public async Task Returns_text_and_restores_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "original" };
        var text = await Create(clipboard, new FakeInput(clipboard)).TryAcquireAsync(CancellationToken.None);

        Assert.Equal("selected text", text);
        Assert.True(clipboard.Restored);
    }

    [Fact]
    public async Task Returns_null_and_skips_copy_when_snapshot_fails()
    {
        var clipboard = new FakeClipboard { SnapshotResult = new ClipboardSnapshot(false, false, null) };
        var input = new FakeInput(clipboard);

        var text = await Create(clipboard, input).TryAcquireAsync(CancellationToken.None);

        Assert.Null(text);
        Assert.False(input.Called);
        Assert.False(clipboard.Restored);
    }

    [Fact]
    public async Task Returns_null_when_nothing_is_copied_and_does_not_restore()
    {
        var clipboard = new FakeClipboard { Text = "original" };
        var input = new FakeInput(clipboard, text: null);
        // 模拟复制没有改变序列号
        var acquirer = new ClipboardTextAcquirer(
            new NoChangeClipboard(clipboard),
            new NoChangeInput(),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(20));

        Assert.Null(await acquirer.TryAcquireAsync(CancellationToken.None));
        Assert.False(clipboard.Restored);
    }

    [Fact]
    public async Task Does_not_restore_when_clipboard_changed_again()
    {
        var clipboard = new FakeClipboard { Text = "original" };
        clipboard.OnRead = () => clipboard.Sequence = 3;

        var text = await Create(clipboard, new FakeInput(clipboard)).TryAcquireAsync(CancellationToken.None);

        Assert.Equal("selected text", text);
        Assert.False(clipboard.Restored);
    }

    [Fact]
    public async Task Does_not_restore_when_snapshot_had_no_data()
    {
        var clipboard = new FakeClipboard { SnapshotResult = new ClipboardSnapshot(true, false, null) };
        await Create(clipboard, new FakeInput(clipboard)).TryAcquireAsync(CancellationToken.None);

        Assert.False(clipboard.Restored);
    }

    private sealed class NoChangeClipboard : IClipboardService
    {
        private readonly FakeClipboard _inner;
        public NoChangeClipboard(FakeClipboard inner) => _inner = inner;

        public uint GetSequenceNumber() => 1;
        public bool TryGetText(out string text) { text = string.Empty; return false; }
        public ClipboardSnapshot Snapshot() => _inner.SnapshotResult;
        public void Restore(object? snapshot) => _inner.Restore(snapshot);
        public bool TrySetText(string text) => true;
    }

    private sealed class NoChangeInput : IInputSimulator
    {
        public void SendCopy()
        {
        }
    }
}