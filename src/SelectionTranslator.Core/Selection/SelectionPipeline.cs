namespace SelectionTranslator.Core.Selection;

public sealed class SelectionPipeline
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(800);

    private readonly IReadOnlyList<ITextAcquirer> _acquirers;
    private readonly IAcquisitionPolicy _policy;
    private readonly TimeProvider _time;
    private string? _lastText;
    private DateTimeOffset _lastAt;

    public SelectionPipeline(
        IReadOnlyList<ITextAcquirer> acquirers,
        IAcquisitionPolicy policy,
        TimeProvider? timeProvider = null)
    {
        _acquirers = acquirers;
        _policy = policy;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<SelectionResult?> AcquireAsync(
        SelectionContext context,
        CancellationToken cancellationToken)
    {
        foreach (var acquirer in _acquirers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_policy.IsAllowed(acquirer.Name, context))
                continue;

            string? text;

            try
            {
                text = await acquirer.TryAcquireAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            catch (Exception)
            {
                // 单个取词方式失败（权限、剪贴板占用等）不应阻断后续降级。
                continue;
            }

            if (!TextValidator.TryNormalize(text, out var normalized, out _))
                continue;

            var now = _time.GetUtcNow();

            if (normalized == _lastText && now - _lastAt < DuplicateWindow)
                continue;

            _lastText = normalized;
            _lastAt = now;
            return new SelectionResult(normalized, acquirer.Name);
        }

        return null;
    }
}