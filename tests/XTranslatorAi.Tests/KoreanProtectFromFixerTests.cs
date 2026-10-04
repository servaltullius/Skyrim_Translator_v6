using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class KoreanProtectFromFixerTests
{
    // An acronym is read letter by letter: NPC → 엔피시, so NPC를 (the fixer wrote NPC을 from the letter C).
    [Fact]
    public void Apply_ChoosesTheParticleOfAnAcronymByItsLetterNames()
    {
        var source = "I was tasked with protecting the NPC from an incoming bandit attack.";
        var dest = "습격해오는 NPC의 공격으로부터 산적을 보호하라는 임무를 받았다.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("습격해오는 산적의 공격으로부터 NPC를 보호하라는 임무를 받았다.", fixedText);
    }

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

    [Theory]
    [InlineData("Protect the Dragonborn from the Thalmor attack.", "탈모르의 공격으로부터 드래곤본을 보호하라.")]
    [InlineData("Protect the Dragonborn from the Thalmor attack.", "탈모르로부터 드래곤본을 보호하라.")]
    [InlineData("Protect the giant's camp from the Stormcloak attack.", "스톰클록의 공격으로부터 거인족 야영지를 보호하라.")]
    public void Apply_DoesNotSwap_WhenAttackerNounIsOnlyPartOfAWord(string source, string dest)
    {
        // "드래곤본" contains "드래곤" and "거인족" contains "거인", but neither is the attacker here.
        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal(dest, fixedText);
    }

    [Fact]
    public void Apply_StillSwaps_WhenAttackerNounIsFollowedByPlural()
    {
        var source = "Protect the village from the vampire attack.";
        var dest = "마을의 공격으로부터 흡혈귀들을 보호하라.";

        var fixedText = TranslationPostEdits.Apply(targetLang: "korean", sourceText: source, translatedText: dest, enableTemplateFixer: false);

        Assert.Equal("흡혈귀들의 공격으로부터 마을을 보호하라.", fixedText);
    }
}
