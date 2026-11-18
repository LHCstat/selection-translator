using System.Text;
using System.Windows.Automation;
using SelectionTranslator.Core.Diagnostics;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Selection;

public sealed class UiaTextAcquirer : ITextAcquirer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _timeouts;
    private bool _disabled;

    public string Name => "uia";

    public async Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        if (_disabled)
            return null;

        if (!await _gate.WaitAsync(0, cancellationToken))
            return null;

        try
        {
            var work = StaRunner.RunAsync(ReadSelection);

            try
            {
                var result = await work.WaitAsync(Timeout, cancellationToken);
                _timeouts = 0;
                return result;
            }
            catch (TimeoutException)
            {
                // 超时无法终止底层 UIA 调用，线程会留在后台；连续多次后停用以避免无限堆积。
                if (Interlocked.Increment(ref _timeouts) >= 3)
                {
                    _disabled = true;
                    AppLog.Warn("UIA acquisition disabled after repeated timeouts");
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? ReadSelection()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            var fromFocus = TryReadFromElement(focused);
            if (fromFocus is not null)
                return fromFocus;

            NativeMethods.GetCursorPos(out var point);
            var atPoint = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            return TryReadFromElement(atPoint);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryReadFromElement(AutomationElement? element)
    {
        var current = element;

        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            try
            {
                if (current.Current.IsPassword)
                    return null;

                if (current.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject))
                {
                    var ranges = ((TextPattern)patternObject).GetSelection();
                    if (ranges.Length > 0)
                    {
                        var builder = new StringBuilder();
                        foreach (var range in ranges)
                            builder.Append(range.GetText(-1));

                        var text = builder.ToString().Trim();
                        if (text.Length > 0)
                            return text;
                    }
                }

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}