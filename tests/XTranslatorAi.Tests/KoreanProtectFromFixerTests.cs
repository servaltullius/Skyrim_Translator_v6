using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class KoreanProtectFromFixerTests
{
    [Fact]
    public void Apply_FixesProtectFromAttack_RoleInversion()
    {
        var source = "I was tasked with protecting Temptation House from an incoming bandit attack.";
        var dest = "습격해오는 템테이션 하우스의 공격으로부터 산적을 보호하라는 임무를 받았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("습격해오는 산적의 공격으로부터 템테이션 하우스를 보호하라는 임무를 받았다.", fixedText);
    }

    [Fact]
    public void Apply_FixesProtectFromAttack_RoleInversion_ForHaveBeenTaskedVariant()
    {
        var source = "I have been tasked with protecting Temptation House from an incoming bandit attack.";
        var dest = "습격해 오는 템테이션 하우스의 공격으로부터 산적을 보호하라는 임무를 받았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("습격해 오는 산적의 공격으로부터 템테이션 하우스를 보호하라는 임무를 받았다.", fixedText);
    }

    [Fact]
    public void Apply_FixesProtectFromAttack_RoleInversion_WithInvisibleSeparators()
    {
        var source = "I have been tasked with protecting Temptation House from an incoming bandit attack.";
        var dest = "습격해\u200B 오는 템테이션\u2060 하우스의 공격으로부터 산적을 보호하라는 임무를 받았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("습격해 오는 산적의 공격으로부터 템테이션 하우스를 보호하라는 임무를 받았다.", fixedText);
    }

    [Fact]
    public void Apply_FixesProtectFromAttack_RoleInversion_WhenMissingAttackNounAndUsingDirectFrom()
    {
        var source = "I have been tasked with protecting Temptation House from an incoming bandit attack.";
        var dest = "습격해오는 템테이션 하우스들로부터 산적을 보호하라는 임무를 맡았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("습격해오는 산적의 공격으로부터 템테이션 하우스들을 보호하라는 임무를 맡았다.", fixedText);
    }

    [Fact]
    public void Apply_DoesNotChange_WhenAlreadyCorrect()
    {
        var source = "I was tasked with protecting Temptation House from an incoming bandit attack.";
        var dest = "습격해오는 산적의 공격으로부터 템테이션 하우스를 보호하라는 임무를 받았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal(dest, fixedText);
    }

    [Fact]
    public void Apply_FixesProtectFromAttack_IncomingPhraseMisplacedAfterFrom()
    {
        var source = "I was tasked with protecting Temptation House from an incoming bandit attack.";
        var dest = "산적의 공격으로부터 습격해 오는 템테이션 하우스를 보호하라는 임무를 받았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("습격해 오는 산적의 공격으로부터 템테이션 하우스를 보호하라는 임무를 받았다.", fixedText);
    }
}
