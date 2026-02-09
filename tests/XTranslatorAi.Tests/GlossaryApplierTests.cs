using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class GlossaryApplierTests
{
    private static int CountOccurrences(string text, string token)
        => Regex.Matches(text, Regex.Escape(token), RegexOptions.CultureInvariant).Count;

    [Fact]
    public void WordBoundary_DoesNotMatchInsideOtherWords()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: null,
                    SourceTerm: "Fast",
                    TargetTerm: "요새",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var applied = applier.Apply("Rannveig's Fast. Fasten your seatbelt.");
        Assert.Contains("__XT_TERM_G1_0000__", applied.Text, StringComparison.Ordinal);
        Assert.Contains("Fasten", applied.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(applied.TokenToReplacement.ContainsKey("__XT_TERM_G1_0000__"));
    }

    [Fact]
    public void WordBoundary_MatchesPunctuationTerm_WhenTermEndsWithPeriod()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: null,
                    SourceTerm: "A skill beyond the reach of most.",
                    TargetTerm: "대다수가 범접할 수 없는 경지의 기술입니다.",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var input = "A skill beyond the reach of most. Dash forward.";
        var applied = applier.Apply(input);

        Assert.Equal("__XT_TERM_G1_0000__ Dash forward.", applied.Text);
        Assert.Equal("대다수가 범접할 수 없는 경지의 기술입니다.", applied.TokenToReplacement["__XT_TERM_G1_0000__"]);
    }

    [Fact]
    public void ForceToken_ReusesTokensAcrossOccurrences()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: null,
                    SourceTerm: "Draugr",
                    TargetTerm: "드라우그르",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var applied = applier.Apply("Draugr and Draugr");

        Assert.Single(applied.TokenToReplacement);
        Assert.Contains("__XT_TERM_G1_0000__", applied.Text, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(applied.Text, "__XT_TERM_G1_0000__"));
        Assert.Equal("드라우그르", applied.TokenToReplacement["__XT_TERM_G1_0000__"]);
    }

    [Fact]
    public void ForceToken_UsesStableEntryIdTokens()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 123,
                    Category: null,
                    SourceTerm: "bandit",
                    TargetTerm: "산적",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var applied = applier.Apply("bandit");

        Assert.Equal("__XT_TERM_G123_0000__", applied.Text);
        Assert.Equal("산적", applied.TokenToReplacement["__XT_TERM_G123_0000__"]);
    }

    [Fact]
    public void Substring_MatchesCjk()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: null,
                    SourceTerm: "龍",
                    TargetTerm: "드래곤",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.Substring,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var applied = applier.Apply("龍の墓");
        Assert.Contains("__XT_TERM_G1_0000__", applied.Text, StringComparison.Ordinal);
        Assert.Equal("드래곤", applied.TokenToReplacement["__XT_TERM_G1_0000__"]);
    }

    [Fact]
    public void Substring_DoesNotMatchInsideXtTokens()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: null,
                    SourceTerm: "XT",
                    TargetTerm: "엑스티",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.Substring,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var input = "__XT_PH_0000__ XT __XT_PH_0001__";
        var applied = applier.Apply(input);

        // Only the plain-text "XT" should be replaced. Placeholder tokens must remain intact.
        Assert.Contains("__XT_PH_0000__", applied.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_0001__", applied.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_TERM_G1_0000__", applied.Text, StringComparison.Ordinal);
        Assert.Equal("엑스티", applied.TokenToReplacement["__XT_TERM_G1_0000__"]);
    }

    [Fact]
    public void PromptOnly_DoesNotTriggerFromXtTokens()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: null,
                    SourceTerm: "XT",
                    TargetTerm: "엑스티",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.Substring,
                    ForceMode: GlossaryForceMode.PromptOnly,
                    Priority: 10,
                    Note: null
                ),
            }
        );

        var input = "__XT_PH_0000__";
        var applied = applier.Apply(input);

        Assert.Empty(applied.PromptOnlyPairs);
        Assert.Equal(input, applied.Text);
    }

    [Fact]
    public void ForceToken_BuiltInReach_DoesNotReplaceCommonWordReach()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: "장소 및 차원 (Places & Realms)",
                    SourceTerm: "Reach",
                    TargetTerm: "리치",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: "Built-in default glossary"
                ),
            }
        );

        var input = "A skill beyond the reach of most.";
        var applied = applier.Apply(input);

        Assert.Equal(input, applied.Text);
        Assert.Empty(applied.TokenToReplacement);
    }

    [Fact]
    public void ForceToken_BuiltInReach_DoesNotReplaceVerbReachLevel()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: "장소 및 차원 (Places & Realms)",
                    SourceTerm: "Reach",
                    TargetTerm: "리치",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: "Built-in default glossary"
                ),
            }
        );

        var input = "Reach level 10.";
        var applied = applier.Apply(input);

        Assert.Equal(input, applied.Text);
        Assert.Empty(applied.TokenToReplacement);
    }

    [Fact]
    public void ForceToken_BuiltInReach_ReplacesPlaceNameReach()
    {
        var applier = new GlossaryApplier(
            new[]
            {
                new GlossaryEntry(
                    Id: 1,
                    Category: "장소 및 차원 (Places & Realms)",
                    SourceTerm: "Reach",
                    TargetTerm: "리치",
                    Enabled: true,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Priority: 10,
                    Note: "Built-in default glossary"
                ),
            }
        );

        var applied = applier.Apply("Travel in the Reach.");

        Assert.Equal("Travel in the __XT_TERM_G1_0000__.", applied.Text);
        Assert.Equal("리치", applied.TokenToReplacement["__XT_TERM_G1_0000__"]);
    }
}
