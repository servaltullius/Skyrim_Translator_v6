using System;
using System.Collections.Generic;
using System.Reflection;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaHeuristicsTests
{
    [Fact]
    public void IsLikelyUntranslated_ReturnsTrue_ForIdenticalEnglish()
    {
        var type = GetHeuristicsType();
        var result = InvokeStatic<bool>(type, "IsLikelyUntranslated", "Saarthal Amulet", "Saarthal Amulet");
        Assert.True(result);
    }

    [Fact]
    public void FindMissingForceTokenGlossaryTerm_ReturnsEntry_WhenMissing()
    {
        var type = GetHeuristicsType();

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

        var missing = InvokeStatic<GlossaryEntry?>(
            type,
            "FindMissingForceTokenGlossaryTerm",
            "Saarthal Amulet",
            "사르달 아뮬렛",
            glossary
        );
        Assert.NotNull(missing);
        Assert.Equal("Saarthal", missing!.SourceTerm);
        Assert.Equal("사아쌀", missing.TargetTerm);

        var ok = InvokeStatic<GlossaryEntry?>(
            type,
            "FindMissingForceTokenGlossaryTerm",
            "Saarthal Amulet",
            "사아쌀 아뮬렛",
            glossary
        );
        Assert.Null(ok);
    }

    [Fact]
    public void FindDoubledParticleExample_ReturnsPair_WhenPresent()
    {
        var type = GetHeuristicsType();

        var hit = InvokeStatic<string?>(type, "FindDoubledParticleExample", "매지카을를 흡수합니다.");
        Assert.Equal("을를", hit);

        var none = InvokeStatic<string?>(type, "FindDoubledParticleExample", "매지카를 흡수합니다.");
        Assert.Null(none);
    }

    [Fact]
    public void FindHangulParticleMismatchSuggestion_ReturnsSuggestion_WhenMismatch()
    {
        var type = GetHeuristicsType();

        var hit = InvokeStatic<string?>(type, "FindHangulParticleMismatchSuggestion", "매지카을 흡수합니다.");
        Assert.Equal("매지카을 → 매지카를", hit);

        var ok = InvokeStatic<string?>(type, "FindHangulParticleMismatchSuggestion", "매지카를 흡수합니다.");
        Assert.Null(ok);
    }

    [Fact]
    public void FindRomanVowelParticleMismatchSuggestion_ReturnsSuggestion_ForVowelEndingWord()
    {
        var type = GetHeuristicsType();

        var hit = InvokeStatic<string?>(type, "FindRomanVowelParticleMismatchSuggestion", "Magicka을 흡수합니다.");
        Assert.Equal("Magicka을 → Magicka를", hit);

        var none = InvokeStatic<string?>(type, "FindRomanVowelParticleMismatchSuggestion", "Blood을 흡수합니다.");
        Assert.Null(none);
    }

    [Fact]
    public void HasUnresolvedParticleMarkers_ReturnsTrue_ForMarkerStrings()
    {
        var type = GetHeuristicsType();
        var hit = InvokeStatic<bool>(type, "HasUnresolvedParticleMarkers", "매지카을(를) 흡수합니다.");
        Assert.True(hit);

        var ok = InvokeStatic<bool>(type, "HasUnresolvedParticleMarkers", "매지카를 흡수합니다.");
        Assert.False(ok);
    }

    [Fact]
    public void FindDuplicationArtifactExample_ReturnsExample_ForObviousDuplicates()
    {
        var type = GetHeuristicsType();

        var effect = InvokeStatic<string?>(type, "FindDuplicationArtifactExample", "치명적인 마법부여 효과 효과가 발동합니다.");
        Assert.Equal("효과 효과", effect);

        var seconds = InvokeStatic<string?>(type, "FindDuplicationArtifactExample", "<dur>초 초 동안 마비시킵니다.");
        Assert.Equal("초 초", seconds);

        var none = InvokeStatic<string?>(type, "FindDuplicationArtifactExample", "<dur>초 동안 마비시킵니다.");
        Assert.Null(none);
    }

    [Fact]
    public void FindPercentArtifactExample_ReturnsExample_ForPercentNoise()
    {
        var type = GetHeuristicsType();

        var points = InvokeStatic<string?>(type, "FindPercentArtifactExample", "10%포인트의 추가 방어 보호를 얻습니다.");
        Assert.Equal("10%포인트", points);

        var hangul = InvokeStatic<string?>(type, "FindPercentArtifactExample", "방패 피해가 밀어치기% 증가합니다.");
        Assert.Equal("밀어치기%", hangul);

        var none = InvokeStatic<string?>(type, "FindPercentArtifactExample", "공격 시 10% 확률로 무장을 해제합니다.");
        Assert.Null(none);
    }

    private static Type GetHeuristicsType()
    {
        var type = Type.GetType("XTranslatorAi.Core.Text.LqaHeuristics, XTranslatorAi.Core");
        Assert.NotNull(type);
        return type!;
    }

    private static T InvokeStatic<T>(Type type, string methodName, params object[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, args);
        return result is T typed ? typed : default!;
    }
}
