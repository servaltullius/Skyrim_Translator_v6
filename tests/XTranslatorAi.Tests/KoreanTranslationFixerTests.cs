using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class KoreanTranslationFixerTests
{
    [Fact]
    public void Fix_ParticleStepExtraction_KeepsAllParticleCasesPassing()
    {
        var cases = new (string Input, string Expected)[]
        {
            ("체력 을(를) 흡수합니다.", "체력을 흡수합니다."),
            ("Aela은(는) 동료입니다.", "Aela는 동료입니다."),
            ("매지카을 흡수합니다.", "매지카를 흡수합니다."),
            ("블러드 을 흡수합니다.", "블러드를 흡수합니다."),
            ("방패은 부서집니다.", "방패는 부서집니다."),
            ("아니요. 저는은 솔리튜드에서 삽니다.", "아니요. 저는 솔리튜드에서 삽니다."),
        };

        foreach (var item in cases)
        {
            var output = KoreanTranslationFixer.Fix("korean", item.Input);
            Assert.Equal(item.Expected, output);
        }
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle()
    {
        var input = "체력 을(를) 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력을 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle_ForVowelEndingNoun()
    {
        var input = "매지카을(를) 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle_ParenFirstStyle()
    {
        var input = "매지카(을)를 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle()
    {
        var input = "체력은(는) 회복됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력은 회복됩니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle_ForVowelEndingNoun()
    {
        var input = "매지카 은(는) 회복됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카는 회복됩니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle_ParenFirstStyle()
    {
        var input = "매지카(은)는 회복됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카는 회복됩니다.", output);
    }

    [Fact]
    public void Fix_ChoosesSubjectParticle_ForParenthesizedParticle_ParenFirstStyle()
    {
        var input = "체력(이)가 감소합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력이 감소합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenWrong()
    {
        var input = "매지카을 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsSeparatedObjectParticle_WhenWrong()
    {
        var input = "블러드 을 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("블러드를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle_ForLatinNoun()
    {
        var input = "Aela을(를) 만났다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("Aela를 만났다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_ForLatinNoun_WhenWrong()
    {
        var input = "Aela을 만났다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("Aela를 만났다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_ForLatinNoun_WhenConsonantEnding()
    {
        var input = "Skyrim를 탐험합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("Skyrim을 탐험합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle_ForLatinNoun()
    {
        var input = "Aela은(는) 동료입니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("Aela는 동료입니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenFollowedByPunctuation()
    {
        var input = "매지카을… 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를… 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenSeparatedByZeroWidthSpace()
    {
        var input = "매지카\u200B을 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_ForConsonantEndingNoun_WhenWrong()
    {
        var input = "검를 듭니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("검을 듭니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenFollowedByEmDash()
    {
        var input = "검를— 듭니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("검을— 듭니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedTopicParticle_WhenWrong()
    {
        var input = "방패은 부서집니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("방패는 부서집니다.", output);
    }

    [Fact]
    public void Fix_ReplacesStatDativeParticle()
    {
        var input = "체력에게 <mag>포인트의 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력에 <mag>포인트의 피해를 입힙니다.", output);
    }

    [Fact]
    public void Fix_ReplacesStatDativeParticle_WithSuffix()
    {
        var input = "매지카에게는 그 절반의 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("ko", input);
        Assert.Contains("매지카에는", output);
        Assert.DoesNotContain("매지카에게는", output);
    }

    [Fact]
    public void Fix_ReplacesStatDativeParticle_FromForm()
    {
        var input = "지구력에게서 <mag>포인트를 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("지구력에서 <mag>포인트를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsSeparatedSubjectParticle_BasedOnBatchim()
    {
        var input = "중갑 가 <mag>포인트 강화됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Contains("중갑이", output);
        Assert.DoesNotContain("중갑 가", output);
    }

    [Fact]
    public void Fix_ReordersMisplacedDurationTokenAfterTimePhrase()
    {
        var input = "늑대인간초 동안 <150>초 의 형상을 취합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("<150>초 동안 늑대인간의 형상을 취합니다.", output);
    }

    [Fact]
    public void Fix_DurationAndArtifactExtraction_KeepsComplexStringsStable()
    {
        var cases = new (string Input, string Expected)[]
        {
            ("피해를 입으면 <25%> <5>초 동안 확률로 투명화하기 상태가 됩니다.", "피해를 입으면 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다."),
            ("피해를 입을 시 <25%> 확률로 <5>초 동안 투명화하기 합니다.", "피해를 입을 시 <25%> 확률로 <5>초 동안 투명화합니다."),
            ("습격이다! 무기를 물건 전달!", "습격이다! 무기를 내려!"),
            ("<15>포인트 체력포인트를 흡수하고 <7><3>초 동안 포인트초포인트의 출혈 피해를 입힙니다.", "<15>포인트 체력을 흡수하고 <3>초 동안 <7>포인트의 출혈 피해를 입힙니다."),
            ("늑대인간초 동안 <150>초 의 형상을 취합니다.", "<150>초 동안 늑대인간의 형상을 취합니다."),
        };

        foreach (var item in cases)
        {
            var output = KoreanTranslationFixer.Fix("korean", item.Input);
            Assert.Equal(item.Expected, output);
        }
    }

    [Fact]
    public void Fix_CleansUpNumericPlaceholderUnitGarbage_ForBleedingAbsorbString()
    {
        var input = "<15>포인트 체력포인트를 흡수하고 <7><3>초 동안 포인트초포인트의 출혈 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("<15>포인트 체력을 흡수하고 <3>초 동안 <7>포인트의 출혈 피해를 입힙니다.", output);
    }

    [Fact]
    public void Fix_DoesNotRewriteAttributiveNeun_AsTopicParticle()
    {
        var input = "달이 떠있는 동안 <mag> 의 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Contains("떠있는 동안", output);
        Assert.DoesNotContain("떠있은", output);
    }

    [Fact]
    public void Fix_CollapsesDuplicateEffectWord()
    {
        var input = "치명적인 마법부여 효과 효과가 적을 비틀거리게 합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("치명적인 마법부여 효과가 적을 비틀거리게 합니다.", output);
    }

    [Fact]
    public void Fix_RemovesRedundantStatPointsNoun_ForStaminaAbsorbString()
    {
        var input = "일정 확률로 적을 비틀거리게 만들며 <15>포인트 지구력포인트를 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("일정 확률로 적을 비틀거리게 만들며 <15>포인트 지구력을 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ReordersProbabilityMarker_WhenItAppearsAfterDuration()
    {
        var input = "피해를 입으면 <25%> <5>초 동안 확률로 투명화 상태가 됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("피해를 입으면 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다.", output);
    }

    [Fact]
    public void Fix_CleansUpHaGiInfinitive_BeforeStateNoun()
    {
        var input = "피해를 입었을 때 <25%> 확률로 <5>초 동안 투명화하기 상태가 됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("피해를 입었을 때 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다.", output);
    }

    [Fact]
    public void Fix_CleansUpHaGiInfinitive_BeforeHapNidaVerb()
    {
        var input = "피해를 입을 시 <25%> 확률로 <5>초 동안 투명화하기 합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("피해를 입을 시 <25%> 확률로 <5>초 동안 투명화합니다.", output);
    }

    [Fact]
    public void Fix_RewritesDropYourWeaponsArtifact()
    {
        var input = "습격이다! 무기를 물건 전달!";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("습격이다! 무기를 내려!", output);
    }

    [Fact]
    public void Fix_CollapsesDuplicateTopicParticle_AfterPronoun()
    {
        var input = "아니요. 저는은 솔리튜드에서 삽니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("아니요. 저는 솔리튜드에서 삽니다.", output);
    }

    [Fact]
    public void Fix_DoesNothing_ForNonKorean()
    {
        var input = "체력에게 <mag> points";
        var output = KoreanTranslationFixer.Fix("english", input);
        Assert.Equal(input, output);
    }
}
