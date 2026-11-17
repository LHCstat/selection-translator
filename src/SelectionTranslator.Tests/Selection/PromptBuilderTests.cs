using SelectionTranslator.Core.Translation;

namespace SelectionTranslator.Tests.Selection;

public class PromptBuilderTests
{
    [Fact]
    public void System_prompt_contains_target_language()
    {
        var messages = PromptBuilder.Build(new TranslationRequest("hello", null, "简体中文", null));
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("简体中文", messages[0].Content);
    }

    [Fact]
    public void User_prompt_is_raw_text_without_context()
    {
        var messages = PromptBuilder.Build(new TranslationRequest("hello", null, "简体中文", null));
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("hello", messages[1].Content);
    }

    [Fact]
    public void Context_goes_to_system_prompt_not_user_text()
    {
        var messages = PromptBuilder.Build(new TranslationRequest("hello", null, "简体中文", "BBC News"));
        Assert.Contains("BBC News", messages[0].Content);
        Assert.Equal("hello", messages[1].Content);
    }
}