using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class TranslationPromptContextTests
{
    [Fact]
    public void BuildUserPrompt_WhenCtxProvided_IncludesCtxFieldInPayloadJson()
    {
        var prompt = TranslationPrompt.BuildUserPrompt(
            sourceLang: "english",
            targetLang: "korean",
            items: new[]
            {
                new TranslationItem(Id: 123, Text: "Hello.", Rec: "INFO:NAM1", Ctx: "Prev (reference only):\n- Hi."),
            },
            promptOnlyGlossary: Array.Empty<(string Source, string Target)>()
        );

        Assert.Contains("\"ctx\":", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildUserPrompt_WhenCtxIsNull_DoesNotIncludeCtxFieldInPayloadJson()
    {
        var prompt = TranslationPrompt.BuildUserPrompt(
            sourceLang: "english",
            targetLang: "korean",
            items: new[]
            {
                new TranslationItem(Id: 123, Text: "Hello.", Rec: "INFO:NAM1"),
            },
            promptOnlyGlossary: Array.Empty<(string Source, string Target)>()
        );

        Assert.DoesNotContain("\"ctx\":", prompt, StringComparison.Ordinal);
    }
}

