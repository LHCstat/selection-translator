using SelectionTranslator.Core.Interop;

namespace SelectionTranslator.Tests.Selection;

public class HotkeyParserTests
{
    [Fact]
    public void Parses_alt_d()
    {
        Assert.True(HotkeyParser.TryParse("Alt+D", out var hotkey));
        Assert.Equal(HotkeyParser.ModAlt, hotkey.Modifiers);
        Assert.Equal((uint)'D', hotkey.VirtualKey);
    }

    [Fact]
    public void Parses_multiple_modifiers_and_function_key()
    {
        Assert.True(HotkeyParser.TryParse("ctrl + shift + F5", out var hotkey));
        Assert.Equal(HotkeyParser.ModControl | HotkeyParser.ModShift, hotkey.Modifiers);
        Assert.Equal(0x74u, hotkey.VirtualKey);
    }

    [Fact]
    public void Rejects_missing_modifier()
    {
        Assert.False(HotkeyParser.TryParse("D", out _));
    }

    [Fact]
    public void Rejects_unknown_key()
    {
        Assert.False(HotkeyParser.TryParse("Alt+Banana", out _));
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.False(HotkeyParser.TryParse(null, out _));
        Assert.False(HotkeyParser.TryParse("", out _));
    }
}