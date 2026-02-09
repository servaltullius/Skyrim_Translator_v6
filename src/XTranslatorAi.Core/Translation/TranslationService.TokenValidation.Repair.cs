using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static bool TryRepairTokens(
        string inputText,
        string outputText,
        IReadOnlyDictionary<string, string>? glossaryTokenToReplacement,
        out string repaired
    )
    {
        var expected = ExtractTokens(inputText);
        if (expected.Count == 0)
        {
            repaired = outputText;
            return true;
        }

        var actual = ExtractTokens(outputText);
        if (actual.Count == 0)
        {
            repaired = "";
            return false;
        }

        if (Math.Abs(expected.Count - actual.Count) > 12)
        {
            repaired = "";
            return false;
        }

        if (actual.Count == expected.Count)
        {
            var idx = 0;
            repaired = XtTokenRegex.Replace(outputText, _ => expected[idx++]);
            return true;
        }

        repaired = RepairTokenCountMismatchGreedy(inputText, expected, outputText, actual, glossaryTokenToReplacement);
        return repaired.Length > 0;
    }

    private static string RepairTokenCountMismatchGreedy(
        string inputText,
        IReadOnlyList<string> expectedTokens,
        string outputText,
        IReadOnlyList<string> actualTokens,
        IReadOnlyDictionary<string, string>? glossaryTokenToReplacement
    )
    {
        SplitByTokens(outputText, out var texts, out var tokens);

        var iExp = 0;
        var jOut = 0;
        const int lookahead = 8;

        while (iExp < expectedTokens.Count && jOut < tokens.Count)
        {
            if (string.Equals(tokens[jOut], expectedTokens[iExp], StringComparison.Ordinal))
            {
                iExp++;
                jOut++;
                continue;
            }

            switch (DecideTokenMismatch(expectedTokens, tokens, iExp, jOut, lookahead))
            {
                case TokenMismatchDecision.DropOutputToken:
                    tokens[jOut] = "";
                    jOut++;
                    break;
                case TokenMismatchDecision.InsertExpectedToken:
                    InsertTokenAtTextBoundary(inputText, texts, jOut, expectedTokens[iExp], glossaryTokenToReplacement);
                    iExp++;
                    break;
                default:
                    // Neither appears later: treat as substitution.
                    tokens[jOut] = expectedTokens[iExp];
                    iExp++;
                    jOut++;
                    break;
            }
        }

        var ctx = new TokenRepairContext(inputText, expectedTokens, tokens, texts, glossaryTokenToReplacement);
        return FinalizeTokenRepair(ctx, iExp, jOut);
    }

    private readonly record struct TokenRepairContext(
        string InputText,
        IReadOnlyList<string> ExpectedTokens,
        List<string> OutputTokens,
        List<string> OutputTexts,
        IReadOnlyDictionary<string, string>? GlossaryTokenToReplacement
    );

    private static string FinalizeTokenRepair(TokenRepairContext ctx, int expectedIndex, int outputIndex)
    {
        while (expectedIndex < ctx.ExpectedTokens.Count)
        {
            InsertTokenAtTextBoundary(
                ctx.InputText,
                ctx.OutputTexts,
                ctx.OutputTexts.Count - 1,
                ctx.ExpectedTokens[expectedIndex],
                ctx.GlossaryTokenToReplacement
            );
            expectedIndex++;
        }

        while (outputIndex < ctx.OutputTokens.Count)
        {
            ctx.OutputTokens[outputIndex] = "";
            outputIndex++;
        }

        return JoinTextAndTokens(ctx.OutputTexts, ctx.OutputTokens);
    }

    private enum TokenMismatchDecision
    {
        SubstituteToken = 0,
        DropOutputToken = 1,
        InsertExpectedToken = 2,
    }

    private static TokenMismatchDecision DecideTokenMismatch(
        IReadOnlyList<string> expectedTokens,
        IReadOnlyList<string> outputTokens,
        int expectedIndex,
        int outputIndex,
        int lookahead
    )
    {
        var expected = expectedTokens[expectedIndex];
        var output = outputTokens[outputIndex];

        var distOut = FindIndex(outputTokens, expected, outputIndex + 1, lookahead);
        var distExp = FindIndex(expectedTokens, output, expectedIndex + 1, lookahead);

        var nextOutHasExpected = distOut >= 0;
        var nextExpHasOutput = distExp >= 0;

        if (nextOutHasExpected && !nextExpHasOutput)
        {
            // Output token is likely extra.
            return TokenMismatchDecision.DropOutputToken;
        }

        if (nextExpHasOutput && !nextOutHasExpected)
        {
            // Expected token is likely missing from output: insert before current output token.
            return TokenMismatchDecision.InsertExpectedToken;
        }

        if (nextOutHasExpected && nextExpHasOutput)
        {
            // Both appear later: pick the cheaper move.
            return (distOut - (outputIndex + 1)) <= (distExp - (expectedIndex + 1))
                ? TokenMismatchDecision.DropOutputToken
                : TokenMismatchDecision.InsertExpectedToken;
        }

        return TokenMismatchDecision.SubstituteToken;
    }

    private static void InsertTokenAtTextBoundary(
        string inputText,
        List<string> texts,
        int boundaryIndex,
        string token,
        IReadOnlyDictionary<string, string>? glossaryTokenToReplacement
    )
    {
        if (boundaryIndex <= 0
            && inputText.StartsWith(token, StringComparison.Ordinal)
            && !texts[0].StartsWith(token, StringComparison.Ordinal))
        {
            texts[0] = token + texts[0];
            return;
        }

        if (glossaryTokenToReplacement != null
            && token.StartsWith("__XT_TERM_", StringComparison.Ordinal)
            && glossaryTokenToReplacement.TryGetValue(token, out var replacement)
            && !string.IsNullOrWhiteSpace(replacement))
        {
            if (TryReplaceFirstOccurrence(texts, boundaryIndex, replacement, token))
            {
                return;
            }

            if (boundaryIndex == texts.Count - 1)
            {
                for (var i = texts.Count - 2; i >= 0; i--)
                {
                    if (TryReplaceFirstOccurrence(texts, i, replacement, token))
                    {
                        return;
                    }
                }
            }
        }

        texts[boundaryIndex] += token;
    }

    private static bool TryReplaceFirstOccurrence(List<string> texts, int index, string needle, string replacement)
    {
        if (index < 0 || index >= texts.Count)
        {
            return false;
        }

        var text = texts[index];
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
        {
            return false;
        }

        var idx = text.IndexOf(needle, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        texts[index] = text.Substring(0, idx) + replacement + text.Substring(idx + needle.Length);
        return true;
    }

    private static int FindIndex(IReadOnlyList<string> list, string value, int start, int maxLookahead)
    {
        if (start < 0)
        {
            start = 0;
        }
        var end = Math.Min(list.Count, start + maxLookahead);
        for (var i = start; i < end; i++)
        {
            if (string.Equals(list[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }
}

