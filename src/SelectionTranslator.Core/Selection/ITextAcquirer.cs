namespace SelectionTranslator.Core.Selection;

public interface ITextAcquirer
{
    string Name { get; }
    Task<string?> TryAcquireAsync(CancellationToken cancellationToken);
}