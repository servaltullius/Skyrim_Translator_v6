using System.Collections.Generic;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.KoreanFix.Internal;
using XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;
using Xunit;

namespace XTranslatorAi.Tests;

public class KoreanTranslationFixerCharacterizationTests
{
    [Fact]
    public void Fix_PipelineSkeleton_InternalContracts_AreAvailable()
    {
        _ = typeof(KoreanFixContext);
        _ = typeof(IKoreanFixStep);
    }

    [Fact]
    public void Fix_ParticleStepExtraction_InternalContracts_AreAvailable()
    {
        _ = typeof(KoreanParticleSelector);
        _ = typeof(ParenthesizedParticleStep);
        _ = typeof(AttachedSeparatedParticleStep);
    }

    [Fact]
    public void Fix_DurationArtifactStepExtraction_InternalContracts_AreAvailable()
    {
        _ = typeof(DurationProbabilityStep);
        _ = typeof(ArtifactCleanupStep);
    }

    [Fact]
    public void Fix_FinalConsolidation_InternalContracts_AreAvailable()
    {
        _ = typeof(StatAndSubjectParticleStep);

        var legacyStep = typeof(KoreanTranslationFixer).Assembly.GetType("XTranslatorAi.Core.Text.KoreanFix.Internal.Steps.LegacyFixStep");
        Assert.Null(legacyStep);
    }

    public static IEnumerable<object[]> RepresentativeVectors()
    {
        yield return new object[] { "체력 을(를) 흡수합니다.", "체력을 흡수합니다." };
        yield return new object[] { "Aela은(는) 동료입니다.", "Aela는 동료입니다." };
        yield return new object[] { "Skyrim를 탐험합니다.", "Skyrim을 탐험합니다." };
        yield return new object[] { "매지카\u200B을 흡수합니다.", "매지카를 흡수합니다." };
        yield return new object[] { "피해를 입으면 <25%> <5>초 동안 확률로 투명화하기 상태가 됩니다.", "피해를 입으면 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다." };
        yield return new object[] { "<15>포인트 체력포인트를 흡수하고 <7><3>초 동안 포인트초포인트의 출혈 피해를 입힙니다.", "<15>포인트 체력을 흡수하고 <3>초 동안 <7>포인트의 출혈 피해를 입힙니다." };
        yield return new object[] { "습격이다! 무기를 물건 전달!", "습격이다! 무기를 내려!" };
        yield return new object[] { "아니요. 저는은 솔리튜드에서 삽니다.", "아니요. 저는 솔리튜드에서 삽니다." };
        yield return new object[] { "달이 떠있는 동안 <mag> 의 피해를 입힙니다.", "달이 떠있는 동안 <mag> 의 피해를 입힙니다." };
    }

    [Theory]
    [MemberData(nameof(RepresentativeVectors))]
    public void Fix_Characterization_RepresentativeVectors(string input, string expected)
    {
        var output = KoreanTranslationFixer.Fix("ko-KR", input);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void Fix_PipelineSkeleton_StillMatchesCharacterization()
    {
        foreach (var row in RepresentativeVectors())
        {
            var input = (string)row[0];
            var expected = (string)row[1];
            var output = KoreanTranslationFixer.Fix("ko-KR", input);
            Assert.Equal(expected, output);
        }
    }

    [Fact]
    public void Fix_Characterization_DoesNothing_ForNonKoreanLanguageTag()
    {
        var input = "체력에게 <mag> points";
        var output = KoreanTranslationFixer.Fix("english", input);
        Assert.Equal(input, output);
    }
}
