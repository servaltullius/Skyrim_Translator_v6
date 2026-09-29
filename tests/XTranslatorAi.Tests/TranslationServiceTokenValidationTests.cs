using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class TranslationServiceTokenValidationTests
{
    [Fact]
    public void EnsureTokensPreservedOrRepair_SwapsMagDur_WhenClearlyMisplaced()
    {
        var input = "Magicka regenerates __XT_PH_MAG_0000__% slower for __XT_PH_DUR_0001__ seconds.";
        var output = "매지카__XT_PH_MAG_0000__초 동안 재생 속도가 __XT_PH_DUR_0001__% 느려집니다.";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test");
        Assert.Contains("__XT_PH_DUR_0001__초", repaired, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_MAG_0000__%", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureTokensPreservedOrRepair_DoesNotSwapMagDur_WhenAlreadyCorrect()
    {
        var input = "Carry weight is reduced by __XT_PH_MAG_0000__ for __XT_PH_DUR_0001__.";
        var output = "__XT_PH_DUR_0001__ 동안 소지 중량이 __XT_PH_MAG_0000__만큼 감소합니다.";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test");
        Assert.Equal(output, repaired);
    }

    [Fact]
    public void EnsureTokensPreservedOrRepair_InsertsMissingGlossaryToken_ByReplacingExistingTranslation()
    {
        var input = "__XT_PH_0000__ __XT_TERM_0000__ Soul";
        var output = "__XT_PH_0000__ 드래곤 영혼";

        var glossaryTokenToReplacement = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__XT_TERM_0000__"] = "드래곤",
        };

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test", glossaryTokenToReplacement);
        Assert.Contains("__XT_TERM_0000__", repaired, StringComparison.Ordinal);
        Assert.DoesNotContain("드래곤", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureTokensPreservedOrRepair_MovesDurToken_WhenPlacedAfterKoreanTimePhrase()
    {
        var input = "Take on the form of the Werewolf for __XT_PH_DUR_0000__ seconds.";
        var output = "늑대인간초 동안 __XT_PH_DUR_0000__의 형상을 취합니다.";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test");
        Assert.Contains("__XT_PH_DUR_0000__초 동안", repaired, StringComparison.Ordinal);
        Assert.Contains("늑대인간의", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureTokensPreservedOrRepair_MovesDurToken_EvenWhenDurAlreadyHasSecondsWord()
    {
        var input = "Take on the form of the Werewolf for __XT_PH_DUR_0000__ seconds.";
        var output = "늑대인간초 동안 __XT_PH_DUR_0000__초 의 형상을 취합니다.";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test");
        Assert.Contains("__XT_PH_DUR_0000__초 동안", repaired, StringComparison.Ordinal);
        Assert.Contains("늑대인간의", repaired, StringComparison.Ordinal);
        Assert.DoesNotContain("늑대인간초 동안", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureTokensPreservedOrRepair_MovesDurToken_WhenPlacedAfterKoreanTimePhrase_WithoutEui()
    {
        var input = "Summons a Nord Ghost for __XT_PH_DUR_0000__ seconds wherever the caster is pointing.";
        var output = "시전자가 가리키는 곳에 노드초 동안 __XT_PH_DUR_0000__ 유령을 소환합니다.";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test");
        Assert.Contains("__XT_PH_DUR_0000__초 동안", repaired, StringComparison.Ordinal);
        Assert.DoesNotContain("노드초", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureTokensPreservedOrRepair_StripsBadKoreanParticlesFromNumericTokens()
    {
        var input = "Drains __XT_PH_MAG_0000__ points from stamina.";
        var output = "__XT_PH_MAG_0000__에서 지구력을 흡수합니다.";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test");
        Assert.Contains("__XT_PH_MAG_0000__", repaired, StringComparison.Ordinal);
        Assert.DoesNotContain("__XT_PH_MAG_0000__에서", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateTokensPreserved_AllowsReorder_ForMagDurTokensOnly()
    {
        var input = "Magicka regenerates __XT_PH_MAG_0000__% slower for __XT_PH_DUR_0001__ seconds.";
        var output = "__XT_PH_DUR_0001__초 동안 매지카 재생 속도가 __XT_PH_MAG_0000__% 느려집니다.";

        TokenValidator.ValidateTokensPreserved(input, output, "test");
    }

    [Fact]
    public void ValidateTokensPreserved_DoesNotAllowReorder_ForFixedTokens()
    {
        var input = "A __XT_PH_0000__ B __XT_PH_MAG_0001__ C __XT_PH_0002__ D";
        var output = "A __XT_PH_0000__ B __XT_PH_0002__ C __XT_PH_MAG_0001__ D";

        Assert.Throws<InvalidOperationException>(() =>
            TokenValidator.ValidateTokensPreserved(input, output, "test"));
    }
}
