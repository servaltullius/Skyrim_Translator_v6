using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class TranslationServiceSemanticRepairTriggerTests
{
    [Fact]
    public void NeedsPlaceholderSemanticRepair_Triggers_WhenNumericTokenIsUsedWithKoreanParticles()
    {
        var input = "A stream of cold that does __XT_PH_MAG_0000__ points of damage per second to Health and Stamina.";
        var output = "__XT_PH_MAG_0000__와(과) 체력에게 초당 지구력만큼의 냉기 피해를 주는 냉기 줄기입니다.";

        var needsRepair = TokenSanitizer.NeedsPlaceholderSemanticRepair(
            input, output, "ko-KR", PlaceholderSemanticRepairMode.Strict);
        Assert.True(needsRepair);
    }

    /// <summary>
    /// "주고·일격·분노" after a number are words, not the time units 주·일·분: the Arcane trial's "피해를 &lt;mag&gt; 주고"
    /// was sent back for repair (and in the GUI, paid for again).
    /// </summary>
    [Theory]
    [InlineData("근접 범위 안의 적에게 물 피해를 __XT_PH_MAG_0000__ 주고, 넘어뜨립니다.")]
    [InlineData("적에게 화염 피해를 __XT_PH_MAG_0000__ 주는 불덩이입니다.")]
    [InlineData("대상에게 __XT_PH_MAG_0000__ 일격을 가합니다.")]
    [InlineData("대상의 분노가 __XT_PH_MAG_0000__ 분노 수치만큼 오릅니다.")]
    public void NeedsPlaceholderSemanticRepair_DoesNotReadAWordAfterAMagnitudeAsATimeUnit(string output)
    {
        Assert.False(TokenSanitizer.NeedsPlaceholderSemanticRepair(
            "Deals __XT_PH_MAG_0000__ Water damage.", output, "ko-KR", PlaceholderSemanticRepairMode.Strict));
    }

    [Theory]
    [InlineData("__XT_PH_MAG_0000__초 동안 화염 피해를 줍니다.")]
    [InlineData("__XT_PH_MAG_0000__초간 화염 피해를 줍니다.")]
    [InlineData("__XT_PH_MAG_0000__ 주 동안 화염 피해를 줍니다.")]
    [InlineData("__XT_PH_MAG_0000__일마다 화염 피해를 줍니다.")]
    public void NeedsPlaceholderSemanticRepair_StillCatchesAMagnitudeUsedAsATime(string output)
    {
        Assert.True(TokenSanitizer.NeedsPlaceholderSemanticRepair(
            "Deals __XT_PH_MAG_0000__ Fire damage.", output, "ko-KR", PlaceholderSemanticRepairMode.Strict));
    }

    [Theory]
    [InlineData("__XT_PH_DUR_0000__초 동안 불탑니다.")]
    [InlineData("__XT_PH_DUR_0000__초간 불탑니다.")]
    [InlineData("__XT_PH_DUR_0000__초마다 불탑니다.")]
    [InlineData("__XT_PH_DUR_0000__초입니다.")]
    [InlineData("__XT_PH_DUR_0000__일째 불탑니다.")]
    [InlineData("__XT_PH_DUR_0000__분의 시간이 걸립니다.")]
    public void NeedsPlaceholderSemanticRepair_AcceptsADurationFollowedByAUnit(string output)
    {
        Assert.False(TokenSanitizer.NeedsPlaceholderSemanticRepair(
            "Deals fire damage for __XT_PH_DUR_0000__ seconds.", output, "ko-KR", PlaceholderSemanticRepairMode.Strict));
    }

    [Theory]
    [InlineData("__XT_PH_DUR_0000__ 주고 불탑니다.")]
    [InlineData("__XT_PH_DUR_0000__ 일격을 가합니다.")]
    public void NeedsPlaceholderSemanticRepair_FlagsADurationFollowedByAWordInsteadOfAUnit(string output)
    {
        Assert.True(TokenSanitizer.NeedsPlaceholderSemanticRepair(
            "Deals fire damage for __XT_PH_DUR_0000__ seconds.", output, "ko-KR", PlaceholderSemanticRepairMode.Strict));
    }
}
