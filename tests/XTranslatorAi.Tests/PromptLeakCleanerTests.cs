using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The translation pipeline cleans echoed prompt instructions with the masked source, which holds "__XT_"
/// whenever a row has a tag or a term; the cleaner took that for a source about placeholders and never ran.
/// </summary>
public class PromptLeakCleanerTests
{
    [Fact]
    public void MaskedRowWithTokens_LosesTheEchoedInstruction()
    {
        const string masked = "Bring the __XT_TERM_0000__ to __XT_PH_VAR_0001__.";
        const string translated = "__XT_TERM_0000__을 __XT_PH_VAR_0001__에게 가져가라. Do not modify placeholder tokens such as __XT_PH_0000__.";

        Assert.Equal("__XT_TERM_0000__을 __XT_PH_VAR_0001__에게 가져가라.", PromptLeakCleaner.StripLeakedPlaceholderInstructions(masked, translated));
        Assert.Equal("__XT_TERM_0000__을 __XT_PH_VAR_0001__에게 가져가라.", TokenSanitizer.SanitizeModelTranslationText(translated, masked));
    }

    // The row's own token comes before the Korean leak; cutting there would drop the whole translation.
    [Fact]
    public void TokenFromTheSource_IsNotTakenForTheStartOfTheLeak()
    {
        const string masked = "Bring me the __XT_TERM_0000__.";
        const string translated = "__XT_TERM_0000__을 가져와. 자리표시자 토큰은 그대로 유지하세요.";

        Assert.Equal("__XT_TERM_0000__을 가져와.", PromptLeakCleaner.StripLeakedPlaceholderInstructions(masked, translated));
    }

    [Fact]
    public void SourceAboutPlaceholders_IsLeftAlone()
    {
        const string masked = "Do not modify the placeholder __XT_PH_VAR_0000__.";
        const string translated = "자리표시자 __XT_PH_VAR_0000__을 변경하지 마라. Do not modify placeholder tokens.";

        Assert.Equal(translated, PromptLeakCleaner.StripLeakedPlaceholderInstructions(masked, translated));
    }

    [Fact]
    public void TranslationWithoutLeak_IsUnchanged()
    {
        const string masked = "Bring the __XT_TERM_0000__ to __XT_PH_VAR_0001__.";
        const string translated = "__XT_TERM_0000__을 __XT_PH_VAR_0001__에게 가져가라.";

        Assert.Equal(translated, PromptLeakCleaner.StripLeakedPlaceholderInstructions(masked, translated));
    }
}
