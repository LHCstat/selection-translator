using System.Text;
using System.Windows.Automation;
using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Core.Selection;

public sealed class UiaTextAcquirer : ITextAcquirer
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    public string Name => "uia";

    public async Task<string?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var work = StaRunner.RunAsync(ReadSelection);

        try
        {
            return await work.WaitAsync(Timeout, cancellationToken);
        }
        catch (Exception)
        {
            return null;
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