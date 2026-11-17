using SelectionTranslator.Core.Selection;

namespace SelectionTranslator.Tests.Selection;

public class TextValidatorTests
{
    [Fact]
    public void Trims_and_accepts_english()
    {
        Assert.True(TextValidator.TryNormalize("  hello world \n", out var text, out var reason));
        Assert.Equal("hello world", text);
        Assert.Null(reason);
    }

    [Fact]
    public void Rejects_empty_or_whitespace()
    {
        Assert.False(TextValidator.TryNormalize("   ", out _, out var reason));
        Assert.Equal("empty", reason);
        Assert.False(TextValidator.TryNormalize(null, out _, out _));
    }

    [Fact]
    public void Rejects_text_over_5000_chars()
    {
        var raw = new string('a', TextValidator.MaxLength + 1);
        Assert.False(TextValidator.TryNormalize(raw, out _, out var reason));
        Assert.Equal("too-long", reason);
    }

    [Fact]
    public void Rejects_digits_and_symbols_without_letters()
    {
        Assert.False(TextValidator.TryNormalize("12345 !!!", out _, out var reason));
        Assert.Equal("no-letter", reason);
    }

    [Fact]
    public void Accepts_chinese_and_mixed_text()
    {
        Assert.True(TextValidator.TryNormalize("你好 hello", out var text, out _));
        Assert.Equal("你好 hello", text);
    }
}