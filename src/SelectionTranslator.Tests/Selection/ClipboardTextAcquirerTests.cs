using SelectionTranslator.Core.Interop;
using SelectionTranslator.Core.Selection;

namespace SelectionTranslator.Tests.Selection;

public class ClipboardTextAcquirerTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public uint Sequence { get; set; } = 1;
        public string Text { get; set; } = string.Empty;
        public object? SnapshotValue { get; set; } = "previous";
        public bool Restored { get; private set; }
        public bool CopyHappens { get; set; } = true;

        public uint GetSequenceNumber() => Sequence;
        public bool TryGetText(out string text) { text = Text; return !string.IsNullOrEmpty(Text); }
        public object? Snapshot() => SnapshotValue;
        public void Restore(object? snapshot) => Restored = true;
        public bool TrySetText(string text) { Text = text; return true; }
    }

    private sealed class FakeInput : IInputSimulator
    {
        private readonly FakeClipboard _clipboard;
        public FakeInput(FakeClipboard clipboard) => _clipboard = clipboard;

        public void SendCopy()
        {
            if (!_clipboard.CopyHappens) return;
            _clipboard.Sequence++;
            _clipboard.Text = "selected text";
        }
    }

    [Fact]
    public async Task Returns_text_and_restores_clipboard()
    {
        var clipboard = new FakeClipboard { Text = "original" };
        var acquirer = new ClipboardTextAcquirer(
            clipboard,
            new FakeInput(clipboard),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(50));

        var text = await acquirer.TryAcquireAsync(CancellationToken.None);

        Assert.Equal("selected text", text);
        Assert.True(clipboard.Restored);
    }

    [Fact]
    public async Task Returns_null_when_nothing_is_copied()
    {
        var clipboard = new FakeClipboard { Text = "original", CopyHappens = false };
        var acquirer = new ClipboardTextAcquirer(
            clipboard,
            new FakeInput(clipboard),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(20));

        Assert.Null(await acquirer.TryAcquireAsync(CancellationToken.None));
        Assert.True(clipboard.Restored);
    }

    [Fact]
    public async Task Returns_null_when_copied_text_is_empty()
    {
        var fake = new SequenceOnlyClipboard();
        var acquirer = new ClipboardTextAcquirer(
            fake,
            new FakeInput2(fake),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(20));

        Assert.Null(await acquirer.TryAcquireAsync(CancellationToken.None));
    }

    private sealed class SequenceOnlyClipboard : IClipboardService
    {
        public uint GetSequenceNumber() => 2;
        public bool TryGetText(out string text) { text = ""; return false; }
        public object? Snapshot() => null;
        public void Restore(object? snapshot) { }
        public bool TrySetText(string text) => true;
    }

    private sealed class FakeInput2 : IInputSimulator
    {
        public FakeInput2(SequenceOnlyClipboard clipboard) { }
        public void SendCopy() { }
    }
}