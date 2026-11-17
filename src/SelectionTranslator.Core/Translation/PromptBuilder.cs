namespace SelectionTranslator.Core.Translation;

public sealed record ChatMessage(string Role, string Content);

public static class PromptBuilder
{
    public static IReadOnlyList<ChatMessage> Build(TranslationRequest request)
    {
        var system = $"""
            你是专业翻译引擎。将用户提供的文本翻译成{request.TargetLang}。
            规则：
            1. 只输出用户文本的译文，不要解释、不要加引号、不要重复原文。
            2. 保持原文的段落与标点结构。
            3. 专有名词、代码、变量名保留原文。
            4. 如果原文是单词或短语，给出最常用的 1-2 个义项，用「；」分隔。
            5. 如果原文已经是{request.TargetLang}，原样返回。
            """;

        if (!string.IsNullOrWhiteSpace(request.PageContext))
        {
            system += $"""

                当前窗口标题（仅用于理解语境，禁止翻译、引用或输出它）：
                {request.PageContext}
                """;
        }

        return [new ChatMessage("system", system), new ChatMessage("user", request.Text)];
    }
}