using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Selection;

public sealed class ClipboardTextAcquirer : ITextAcquirer
{
    private readonly IClipboardService _clipboard;
    private readonly IInputSimulator _input;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ClipboardTextAcquirer(
        IClipboardService clipboard,
        IInputSimulator input,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null)
    {
        _clipboard = clipboard;
        _input = input;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(10);
        _timeout = timeout ?? TimeSpan.FromMilliseconds(500);
    }

    public string Name => "clipboard";

    public async Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return null;

        var snapshot = _clipboard.Snapshot();
        if (!snapshot.Success)
        {
            // 无法可靠保存原内容时不冒险改写剪贴板。
            _gate.Release();
            return null;
        }

        var sequenceBefore = _clipboard.GetSequenceNumber();
        var ourSequence = 0u;
        var sawCopy = false;

        try
        {
            _input.SendCopy();

            var attempts = Math.Max(1, (int)Math.Ceiling(_timeout.TotalMilliseconds / _pollInterval.TotalMilliseconds));

            for (var i = 0; i < attempts; i++)
            {
                await Task.Delay(_pollInterval, cancellationToken);

                var sequence = _clipboard.GetSequenceNumber();
                if (sequence == sequenceBefore)
                    continue;

                if (sawCopy && sequence != ourSequence)
                {
                    // 我们复制之后又有别的程序改了剪贴板，放弃本次取词，也不覆盖它。
                    return null;
                }

                ourSequence = sequence;
                sawCopy = true;

                if (_clipboard.TryGetText(out var text) && !string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }
        finally
        {
            if (snapshot.HasData && sawCopy && _clipboard.GetSequenceNumber() == ourSequence)
                _clipboard.Restore(snapshot.Data);

            _gate.Release();
        }
    }
}