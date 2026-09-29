using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class SessionTermMemoryForceTokenTests
{
    [Fact]
    public void ReplaceSessionTermsTokenSafe_ReplacesInPlainText_AndBuildsTokenMap()
    {
        var memory = new TranslationService.SessionTermMemory(maxTerms: 200);
        Assert.True(memory.TryLearn("Ancient Dragons' Lightning Spear", "고룡의 뇌창"));

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forcing = memory.GetForcingEntriesForText("Start __XT_PH_0000__ Ancient Dragons' Lightning Spear __XT_PH_0001__ End", excluded);
        Assert.Single(forcing);
        Assert.Equal("__XT_TERM_SESS_0000__", forcing[0].Token);

        var input = "Start __XT_PH_0000__ Ancient Dragons' Lightning Spear __XT_PH_0001__ End";
        var output = TranslationService.ReplaceSessionTermsTokenSafe(input, forcing, out var usedTokenToReplacement);

        Assert.Contains("__XT_PH_0000__", output, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_0001__", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ancient Dragons' Lightning Spear", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__XT_TERM_SESS_0000__", output, StringComparison.Ordinal);
        Assert.Equal("고룡의 뇌창", usedTokenToReplacement["__XT_TERM_SESS_0000__"]);
    }

    [Fact]
    public void ReplaceSessionTermsTokenSafe_ReplacesWithinLongerString()
    {
        var memory = new TranslationService.SessionTermMemory(maxTerms: 200);
        Assert.True(memory.TryLearn("Ancient Dragons' Lightning Spear", "고룡의 뇌창"));

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forcing = memory.GetForcingEntriesForText("Ancient Dragons' Lightning Spear Impact effect", excluded);
        Assert.Single(forcing);

        var input = "Ancient Dragons' Lightning Spear Impact effect";
        var output = TranslationService.ReplaceSessionTermsTokenSafe(input, forcing, out _);

        Assert.Contains("__XT_TERM_SESS_0000__", output, StringComparison.Ordinal);
        Assert.Contains("Impact effect", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ancient Dragons' Lightning Spear", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplaceSessionTermsTokenSafe_StripsLeadingThe_WhenPresent()
    {
        var memory = new TranslationService.SessionTermMemory(maxTerms: 200);
        Assert.True(memory.TryLearn("Ancient Dragons' Lightning Spear", "고룡의 뇌창"));

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forcing = memory.GetForcingEntriesForText("The Ancient Dragons' Lightning Spear", excluded);
        Assert.Single(forcing);

        var input = "The Ancient Dragons' Lightning Spear";
        var output = TranslationService.ReplaceSessionTermsTokenSafe(input, forcing, out _);

        Assert.Equal("__XT_TERM_SESS_0000__", output);
    }

    [Fact]
    public void ReplaceSessionTermsTokenSafe_ForSingleWord_DoesNotMatchInsideOtherWords()
    {
        var memory = new TranslationService.SessionTermMemory(maxTerms: 200);
        Assert.True(memory.TryLearn("Art", "예술"));

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forcing = memory.GetForcingEntriesForText("Artifact Art Artillery", excluded);
        Assert.Single(forcing);
        Assert.Equal("__XT_TERM_SESS_0000__", forcing[0].Token);

        var input = "Artifact Art Artillery";
        var output = TranslationService.ReplaceSessionTermsTokenSafe(input, forcing, out _);

        Assert.Equal("Artifact __XT_TERM_SESS_0000__ Artillery", output);
    }
}
