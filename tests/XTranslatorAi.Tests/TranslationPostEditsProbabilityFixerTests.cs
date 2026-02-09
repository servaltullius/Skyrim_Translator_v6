using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class TranslationPostEditsProbabilityFixerTests
{
    [Fact]
    public void Apply_FixesProbabilityAndPercent_WhenPercentPlaceholderIsFollowedByExtraPercent()
    {
        var source = "A <25%> chance to turn invisible for <5> seconds when taking damage.";
        var dest = "피해를 입을 시 <25%>% <5>초 동안 확률로 투명화 상태가 됩니다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("피해를 입을 시 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다.", fixedText);
    }
}

