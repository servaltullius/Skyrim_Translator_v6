using System.Reflection;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class TranslationServiceSemanticRepairTriggerTests
{
    [Fact]
    public void NeedsPlaceholderSemanticRepair_Triggers_WhenNumericTokenIsUsedWithKoreanParticles()
    {
        var method = typeof(TranslationService).GetMethod(
            "NeedsPlaceholderSemanticRepair",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);

        var input = "A stream of cold that does __XT_PH_MAG_0000__ points of damage per second to Health and Stamina.";
        var output = "__XT_PH_MAG_0000__와(과) 체력에게 초당 지구력만큼의 냉기 피해를 주는 냉기 줄기입니다.";

        var needsRepair = (bool)method!.Invoke(
            null,
            new object?[] { input, output, "ko-KR", PlaceholderSemanticRepairMode.Strict }
        )!;
        Assert.True(needsRepair);
    }
}
