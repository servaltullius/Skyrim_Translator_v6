using System.Collections.Generic;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaHeuristicsTests
{
    [Fact]
    public void IsLikelyUntranslated_ReturnsTrue_ForIdenticalEnglish()
    {
        var result = LqaHeuristics.IsLikelyUntranslated("Saarthal Amulet", "Saarthal Amulet");
        Assert.True(result);
    }

    [Fact]
    public void FindMissingForceTokenGlossaryTerm_ReturnsEntry_WhenMissing()
    {
        var glossary = new List<GlossaryEntry>
        {
            new(
                Id: 1,
                Category: null,
                SourceTerm: "Saarthal",
                TargetTerm: "사아쌀",
                Enabled: true,
                MatchMode: GlossaryMatchMode.WordBoundary,
                ForceMode: GlossaryForceMode.ForceToken,
                Priority: 10,
                Note: null
            ),
        };

        var missing = LqaHeuristics.FindMissingForceTokenGlossaryTerm(
            "Saarthal Amulet",
            "사르달 아뮬렛",
            glossary
        );
        Assert.NotNull(missing);
        Assert.Equal("Saarthal", missing!.SourceTerm);
        Assert.Equal("사아쌀", missing.TargetTerm);

        var ok = LqaHeuristics.FindMissingForceTokenGlossaryTerm(
            "Saarthal Amulet",
            "사아쌀 아뮬렛",
            glossary
        );
        Assert.Null(ok);
    }

    [Fact]
    public void FindDoubledParticleExample_ReturnsPair_WhenPresent()
    {
        var hit = LqaHeuristics.FindDoubledParticleExample("매지카을를 흡수합니다.");
        Assert.Equal("을를", hit);

        var none = LqaHeuristics.FindDoubledParticleExample("매지카를 흡수합니다.");
        Assert.Null(none);
    }

    [Fact]
    public void FindHangulParticleMismatchSuggestion_ReturnsSuggestion_WhenMismatch()
    {
        var hit = LqaHeuristics.FindHangulParticleMismatchSuggestion("매지카을 흡수합니다.");
        Assert.Equal("매지카을 → 매지카를", hit);

        var ok = LqaHeuristics.FindHangulParticleMismatchSuggestion("매지카를 흡수합니다.");
        Assert.Null(ok);
    }

    [Fact]
    public void FindRomanParticleMismatchSuggestion_ReturnsSuggestion_ForAcronym()
    {
        var hit = LqaHeuristics.FindRomanParticleMismatchSuggestion("NPC을 고용합니다.");
        Assert.Equal("NPC을 → NPC를", hit);

        var none = LqaHeuristics.FindRomanParticleMismatchSuggestion("NPC를 고용합니다.");
        Assert.Null(none);
    }

    [Fact]
    public void HasUnresolvedParticleMarkers_ReturnsTrue_ForMarkerStrings()
    {
        var hit = LqaHeuristics.HasUnresolvedParticleMarkers("매지카을(를) 흡수합니다.");
        Assert.True(hit);

        var ok = LqaHeuristics.HasUnresolvedParticleMarkers("매지카를 흡수합니다.");
        Assert.False(ok);
    }

    [Fact]
    public void FindDuplicationArtifactExample_ReturnsExample_ForObviousDuplicates()
    {
        var effect = LqaHeuristics.FindDuplicationArtifactExample("치명적인 마법부여 효과 효과가 발동합니다.");
        Assert.Equal("효과 효과", effect);

        var seconds = LqaHeuristics.FindDuplicationArtifactExample("<dur>초 초 동안 마비시킵니다.");
        Assert.Equal("초 초", seconds);

        var none = LqaHeuristics.FindDuplicationArtifactExample("<dur>초 동안 마비시킵니다.");
        Assert.Null(none);

        Assert.Equal("초 초", LqaHeuristics.FindDuplicationArtifactExample("<dur>초 초가 지나면 폭발합니다."));
        Assert.Equal("효과 효과", LqaHeuristics.FindDuplicationArtifactExample("효과 효과."));
    }

    [Theory]
    [InlineData("<dur>초 초과하면 해제됩니다.")]
    [InlineData("지속시간이 60초 초과입니다.")]
    [InlineData("<dur>초 초기화됩니다.")]
    [InlineData("이 효과 효과적으로 막아냅니다.")]
    public void FindDuplicationArtifactExample_IgnoresWordsThatOnlyStartWithTheRepeatedSyllable(string dest)
        => Assert.Null(LqaHeuristics.FindDuplicationArtifactExample(dest));

    [Fact]
    public void FindPercentArtifactExample_ReturnsExample_ForPercentNoise()
    {
        var points = LqaHeuristics.FindPercentArtifactExample("10%포인트의 추가 방어 보호를 얻습니다.");
        Assert.Equal("10%포인트", points);

        var hangul = LqaHeuristics.FindPercentArtifactExample("방패 피해가 밀어치기% 증가합니다.");
        Assert.Equal("밀어치기%", hangul);

        var none = LqaHeuristics.FindPercentArtifactExample("공격 시 10% 확률로 무장을 해제합니다.");
        Assert.Null(none);
    }
}
