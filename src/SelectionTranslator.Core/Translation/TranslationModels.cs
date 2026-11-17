namespace SelectionTranslator.Core.Translation;

public sealed record TranslationRequest(
    string Text,
    string? SourceLang,
    string TargetLang,
    string? PageContext = null);

public sealed record TranslationChunk(string Delta, bool IsFinal, string? Error = null);

public interface ITranslationProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task TranslateAsync(
        TranslationRequest request,
        IProgress<TranslationChunk> onChunk,
        CancellationToken cancellationToken);
}

public sealed class TranslationException : Exception
{
    public int? StatusCode { get; }

    public TranslationException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}