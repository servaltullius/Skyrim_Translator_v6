using System;
using System.Collections.Generic;
using System.Reflection;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class SessionTermMemoryForceTokenTests
{
    [Fact]
    public void ReplaceSessionTermsTokenSafe_ReplacesInPlainText_AndBuildsTokenMap()
    {
        var memory = CreateSessionTermMemory(maxTerms: 200);
        Assert.True(TryLearn(memory, "Ancient Dragons' Lightning Spear", "고룡의 뇌창"));

        var forcing = GetForcingEntries(memory, "Start __XT_PH_0000__ Ancient Dragons' Lightning Spear __XT_PH_0001__ End");
        Assert.Single(forcing);
        Assert.Equal("__XT_TERM_SESS_0000__", forcing[0].Token);

        var input = "Start __XT_PH_0000__ Ancient Dragons' Lightning Spear __XT_PH_0001__ End";
        var output = ReplaceSessionTermsTokenSafe(input, forcing, out var usedTokenToReplacement);

        Assert.Contains("__XT_PH_0000__", output, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_0001__", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ancient Dragons' Lightning Spear", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__XT_TERM_SESS_0000__", output, StringComparison.Ordinal);
        Assert.Equal("고룡의 뇌창", usedTokenToReplacement["__XT_TERM_SESS_0000__"]);
    }

    [Fact]
    public void ReplaceSessionTermsTokenSafe_ReplacesWithinLongerString()
    {
        var memory = CreateSessionTermMemory(maxTerms: 200);
        Assert.True(TryLearn(memory, "Ancient Dragons' Lightning Spear", "고룡의 뇌창"));

        var forcing = GetForcingEntries(memory, "Ancient Dragons' Lightning Spear Impact effect");
        Assert.Single(forcing);

        var input = "Ancient Dragons' Lightning Spear Impact effect";
        var output = ReplaceSessionTermsTokenSafe(input, forcing, out _);

        Assert.Contains("__XT_TERM_SESS_0000__", output, StringComparison.Ordinal);
        Assert.Contains("Impact effect", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ancient Dragons' Lightning Spear", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplaceSessionTermsTokenSafe_StripsLeadingThe_WhenPresent()
    {
        var memory = CreateSessionTermMemory(maxTerms: 200);
        Assert.True(TryLearn(memory, "Ancient Dragons' Lightning Spear", "고룡의 뇌창"));

        var forcing = GetForcingEntries(memory, "The Ancient Dragons' Lightning Spear");
        Assert.Single(forcing);

        var input = "The Ancient Dragons' Lightning Spear";
        var output = ReplaceSessionTermsTokenSafe(input, forcing, out _);

        Assert.Equal("__XT_TERM_SESS_0000__", output);
    }

    [Fact]
    public void ReplaceSessionTermsTokenSafe_ForSingleWord_DoesNotMatchInsideOtherWords()
    {
        var memory = CreateSessionTermMemory(maxTerms: 200);
        Assert.True(TryLearn(memory, "Art", "예술"));

        var forcing = GetForcingEntries(memory, "Artifact Art Artillery");
        Assert.Single(forcing);
        Assert.Equal("__XT_TERM_SESS_0000__", forcing[0].Token);

        var input = "Artifact Art Artillery";
        var output = ReplaceSessionTermsTokenSafe(input, forcing, out _);

        Assert.Equal("Artifact __XT_TERM_SESS_0000__ Artillery", output);
    }

    private static object CreateSessionTermMemory(int maxTerms)
    {
        var t = typeof(TranslationService).GetNestedType("SessionTermMemory", BindingFlags.NonPublic);
        Assert.NotNull(t);

        var ctor = t!.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, new[] { typeof(int) }, modifiers: null);
        Assert.NotNull(ctor);

        return ctor!.Invoke(new object?[] { maxTerms });
    }

    private static bool TryLearn(object memory, string source, string target)
    {
        var method = memory.GetType().GetMethod("TryLearn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(memory, new object?[] { source, target })!;
    }

    private static IReadOnlyList<(string Source, string Token, string Target)> GetForcingEntries(object memory, string text)
    {
        var method = memory.GetType().GetMethod("GetForcingEntriesForText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (IReadOnlyList<(string Source, string Token, string Target)>)method!.Invoke(memory, new object?[] { text, excluded })!;
    }

    private static string ReplaceSessionTermsTokenSafe(
        string input,
        IReadOnlyList<(string Source, string Token, string Target)> forcing,
        out IReadOnlyDictionary<string, string> usedTokenToReplacement
    )
    {
        var method = typeof(TranslationService).GetMethod(
            "ReplaceSessionTermsTokenSafe",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        Assert.NotNull(method);

        object? outDict = null;
        var args = new object?[] { input, forcing, outDict };
        var output = (string)method!.Invoke(null, args)!;
        usedTokenToReplacement = (IReadOnlyDictionary<string, string>)args[2]!;
        return output;
    }
}
