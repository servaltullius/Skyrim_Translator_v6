using System;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The glossary tab lets a user pick "정규식" as the match mode, and nothing checks the pattern. One entry such as
/// "[Dragon" threw in the GlossaryApplier constructor, which failed the whole translation run and the cost estimate.
/// </summary>
public class GlossaryRegexEntryTests
{
    private static GlossaryEntry Entry(long id, string source, string target, GlossaryMatchMode mode)
        => new(id, null, source, target, Enabled: true, mode, GlossaryForceMode.ForceToken, Priority: 10, Note: null);

    [Fact]
    public void RegexEntry_MatchesAndTokenizes()
    {
        var applier = new GlossaryApplier(new[] { Entry(1, @"Dragon(?:born)?", "드래곤본", GlossaryMatchMode.Regex) });

        var applied = applier.Apply("The Dragonborn arrives.");

        Assert.DoesNotContain("Dragonborn", applied.Text, StringComparison.Ordinal);
        Assert.Contains("드래곤본", applied.TokenToReplacement.Values);
    }

    [Fact]
    public void InvalidRegexEntry_IsSkippedAndReported_OtherEntriesStillApply()
    {
        var applier = new GlossaryApplier(new[]
        {
            Entry(1, "[Dragon", "드래곤", GlossaryMatchMode.Regex),
            Entry(2, "Whiterun", "화이트런", GlossaryMatchMode.WordBoundary),
        });

        var applied = applier.Apply("Dragon over Whiterun.");

        Assert.Equal(new[] { "[Dragon" }, applier.InvalidRegexTerms);
        Assert.Contains("Dragon", applied.Text, StringComparison.Ordinal);
        Assert.Contains("화이트런", applied.TokenToReplacement.Values);
    }

    [Fact]
    public void ValidEntries_ReportNoInvalidTerms()
    {
        var applier = new GlossaryApplier(new[] { Entry(1, "Whiterun", "화이트런", GlossaryMatchMode.WordBoundary) });

        Assert.Empty(applier.InvalidRegexTerms);
    }

    [Theory]
    [InlineData("[Dragon", false)]
    [InlineData("(unclosed", false)]
    [InlineData(@"Dragon(?:born)?", true)]
    [InlineData("", false)]
    public void IsValidRegexPattern_ChecksThePattern(string pattern, bool expected)
        => Assert.Equal(expected, GlossaryApplier.IsValidRegexPattern(pattern));
}
