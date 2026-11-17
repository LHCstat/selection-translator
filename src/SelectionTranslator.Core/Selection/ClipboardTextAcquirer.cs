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

        try
        {
            var sequence = _clipboard.GetSequenceNumber();
            _input.SendCopy();

            var attempts = Math.Max(1, (int)Math.Ceiling(_timeout.TotalMilliseconds / _pollInterval.TotalMilliseconds));

            for (var i = 0; i < attempts; i++)
            {
                await Task.Delay(_pollInterval, cancellationToken);

                if (_clipboard.GetSequenceNumber() == sequence)
                    continue;

                if (_clipboard.TryGetText(out var text) && !string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }
        finally
        {
            _clipboard.Restore(snapshot);
            _gate.Release();
        }
    }
}