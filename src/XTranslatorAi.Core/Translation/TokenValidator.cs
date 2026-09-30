using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

internal static class TokenValidator
{
    // ── Regex ──

    internal static readonly Regex RawSkyrimSemanticPlaceholderRegex = new(
        pattern: @"(?<sign>[+-]?)<\s*(?<kind>mag|dur|bur)\s*>(?<pct>\s*%)?",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex MarkupTagNameRegex = new(
        pattern: @"^[+-]?<\s*(?<closing>/)?\s*(?<name>[A-Za-z][A-Za-z0-9:_-]*)(?=[\s=/>])",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly string[] FormattingTagNames =
    {
        "a", "b", "i", "u", "s", "font", "p", "br", "img", "textformat", "li", "ul", "ol",
        "span", "div", "strong", "em", "sub", "sup", "hr", "body", "html",
    };

    // ── Token extraction ──

    internal static void SplitByTokens(string text, out List<string> texts, out List<string> tokens)
    {
        texts = new List<string>();
        tokens = new List<string>();

        var idx = 0;
        foreach (Match m in TranslationConstants.XtTokenRegex.Matches(text))
        {
            if (m.Index > idx)
            {
                texts.Add(text.Substring(idx, m.Index - idx));
            }
            else
            {
                texts.Add("");
            }

            tokens.Add(m.Value);
            idx = m.Index + m.Length;
        }

        if (idx < text.Length)
        {
            texts.Add(text.Substring(idx));
        }
        else
        {
            texts.Add("");
        }
    }

    internal static string JoinTextAndTokens(IReadOnlyList<string> texts, IReadOnlyList<string> tokens)
    {
        var capacity = 0;
        foreach (var t in texts)
        {
            capacity += t.Length;
        }
        foreach (var t in tokens)
        {
            capacity += t.Length;
        }
        var sb = new StringBuilder(capacity: capacity);
        var count = tokens.Count;
        for (var i = 0; i < count; i++)
        {
            sb.Append(texts[i]);
            sb.Append(tokens[i]);
        }
        sb.Append(texts[count]);
        return sb.ToString();
    }

    internal static List<string> ExtractTokens(string text)
    {
        var matches = TranslationConstants.XtTokenRegex.Matches(text);
        var tokens = new List<string>(capacity: matches.Count);
        foreach (Match m in matches)
        {
            tokens.Add(m.Value);
        }
        return tokens;
    }

    internal static Dictionary<string, int> CountTokens(IEnumerable<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (counts.TryGetValue(token, out var n))
            {
                counts[token] = n + 1;
            }
            else
            {
                counts[token] = 1;
            }
        }
        return counts;
    }

    // ── Validation ──

    internal static void ValidateTokensPreserved(string inputText, string outputText, string context)
    {
        var expected = ExtractTokens(inputText);
        var actual = ExtractTokens(outputText);

        if (expected.Count == actual.Count)
        {
            ValidateTokensPreservedSameCount(expected, actual, context);
            return;
        }

        ValidateTokensPreservedDifferentCount(expected, actual, context);
    }

    internal static void ValidateRawTagsPreserved(string inputText, string outputText, string context)
    {
        if (string.IsNullOrWhiteSpace(inputText) || string.IsNullOrWhiteSpace(outputText))
        {
            return;
        }

        if (inputText.IndexOf('<') < 0 && inputText.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        ValidateRawSkyrimSemanticPlaceholdersPreserved(inputText, outputText, context);

        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in TokenSanitizer.RawMarkupTagRegex.Matches(inputText))
        {
            if (!m.Success)
            {
                continue;
            }

            var tag = m.Value;
            if (expectedCounts.TryGetValue(tag, out var n))
            {
                expectedCounts[tag] = n + 1;
            }
            else
            {
                expectedCounts[tag] = 1;
            }
        }

        if (expectedCounts.Count == 0)
        {
            return;
        }

        var actualCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in TokenSanitizer.RawMarkupTagRegex.Matches(outputText))
        {
            if (!m.Success)
            {
                continue;
            }

            var tag = m.Value;
            if (actualCounts.TryGetValue(tag, out var n))
            {
                actualCounts[tag] = n + 1;
            }
            else
            {
                actualCounts[tag] = 1;
            }
        }

        foreach (var (tag, expectedCount) in expectedCounts)
        {
            actualCounts.TryGetValue(tag, out var actualCount);
            if (actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Raw tag count mismatch for {context}: {tag} (expected {expectedCount}, got {actualCount})."
                );
            }
        }

        foreach (var (tag, actualCount) in actualCounts)
        {
            expectedCounts.TryGetValue(tag, out var expectedCount);
            if (actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Unexpected raw tag count for {context}: {tag} (expected {expectedCount}, got {actualCount})."
                );
            }
        }
    }

    internal static void ValidateFinalTextIntegrity(string sourceText, string finalText, string context)
    {
        ValidateTokensPreserved(sourceText, finalText, context);
        ValidateRawTagsPreserved(sourceText, finalText, context);
        ValidateProtectedTextPreserved(sourceText, finalText, context);
    }

    private static void ValidateProtectedTextPreserved(string sourceText, string finalText, string context)
    {
        // Use the same protection contract as API translation, including printf, named
        // variables, newlines and page breaks. TM results bypass the masking pipeline.
        var masker = new PlaceholderMasker();
        var sourceProtected = masker.Mask(sourceText).TokenToOriginal.Values;
        var finalProtected = masker.Mask(finalText).TokenToOriginal.Values;
        var expected = CountTokens(sourceProtected);
        var actual = CountTokens(finalProtected);
        foreach (var (placeholder, count) in expected)
        {
            actual.TryGetValue(placeholder, out var actualCount);
            if (count != actualCount)
            {
                throw new InvalidOperationException(
                    $"Protected text mismatch for {context}: {placeholder} (expected {count}, got {actualCount})."
                );
            }
        }
        foreach (var (placeholder, count) in actual)
        {
            if (!expected.ContainsKey(placeholder))
            {
                throw new InvalidOperationException(
                    $"Unexpected protected text for {context}: {placeholder} (expected 0, got {count})."
                );
            }
        }

        // Runtime values may move with Korean grammar. Formatting tags, page breaks
        // and line breaks must retain their relative order, including custom paired tags.
        var formattingNames = new HashSet<string>(FormattingTagNames, StringComparer.OrdinalIgnoreCase);
        foreach (var placeholder in sourceProtected)
        {
            var tag = MarkupTagNameRegex.Match(placeholder);
            if (tag.Success && tag.Groups["closing"].Success)
            {
                formattingNames.Add(tag.Groups["name"].Value);
            }
        }
        var sourceStructure = ExtractFixedProtectedText(sourceProtected, formattingNames);
        var finalStructure = ExtractFixedProtectedText(finalProtected, formattingNames);
        if (sourceStructure.Count != finalStructure.Count)
        {
            throw new InvalidOperationException($"Protected formatting order mismatch for {context}.");
        }
        for (var i = 0; i < sourceStructure.Count; i++)
        {
            if (!string.Equals(sourceStructure[i], finalStructure[i], StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Protected formatting order mismatch for {context} (position {i}).");
            }
        }
    }

    private static List<string> ExtractFixedProtectedText(IEnumerable<string> placeholders, ISet<string> formattingNames)
    {
        var structure = new List<string>();
        foreach (var placeholder in placeholders)
        {
            var tag = MarkupTagNameRegex.Match(placeholder);
            if (placeholder is "\r\n" or "\r" or "\n"
                || placeholder.Equals("[pagebreak]", StringComparison.OrdinalIgnoreCase)
                || (tag.Success && formattingNames.Contains(tag.Groups["name"].Value)))
            {
                structure.Add(placeholder);
            }
        }
        return structure;
    }

    internal static void ValidateNotTruncatedOrOmitted(string inputText, string outputText, string context)
    {
        if (!string.IsNullOrWhiteSpace(inputText) && string.IsNullOrWhiteSpace(outputText))
            throw new InvalidOperationException($"Translation is empty for {context}.");

        SplitByTokens(inputText, out var inputTexts, out _);
        SplitByTokens(outputText, out var outputTexts, out _);

        if (inputTexts.Count != outputTexts.Count)
        {
            return;
        }

        for (var i = 0; i < inputTexts.Count; i++)
        {
            var inLen = CountLettersOrDigits(inputTexts[i]);
            if (inLen < 240)
            {
                continue;
            }

            var outLen = CountLettersOrDigits(outputTexts[i]);

            var minRatio = inLen >= 800 ? 0.20 : 0.16;
            var minAbs = inLen >= 800 ? 120 : 60;
            var required = Math.Max(minAbs, (int)Math.Ceiling(inLen * minRatio));
            if (outLen < required)
            {
                throw new InvalidOperationException(
                    $"Translation appears to omit content for {context} (segment {i}): input={inLen}, output={outLen}."
                );
            }
        }
    }

    // ── Repair ──

    internal static bool TryRepairTokens(
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
            repaired = TranslationConstants.XtTokenRegex.Replace(outputText, m => idx < expected.Count ? expected[idx++] : m.Value);
            return true;
        }

        repaired = RepairTokenCountMismatchGreedy(inputText, expected, outputText, actual, glossaryTokenToReplacement);
        return repaired.Length > 0;
    }

    // ── Private helpers ──

    private static int CountLettersOrDigits(string s)
    {
        var count = 0;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                count++;
            }
        }
        return count;
    }

    private static void ValidateRawSkyrimSemanticPlaceholdersPreserved(string inputText, string outputText, string context)
    {
        if (string.IsNullOrWhiteSpace(inputText) || string.IsNullOrWhiteSpace(outputText))
        {
            return;
        }

        if (inputText.IndexOf("<mag", StringComparison.OrdinalIgnoreCase) < 0
            && inputText.IndexOf("<dur", StringComparison.OrdinalIgnoreCase) < 0
            && inputText.IndexOf("<bur", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        var expected = CountRawSkyrimSemanticPlaceholders(inputText);
        if (expected.Count == 0)
        {
            return;
        }

        var actual = CountRawSkyrimSemanticPlaceholders(outputText);

        foreach (var (key, expectedCount) in expected)
        {
            actual.TryGetValue(key, out var actualCount);
            if (actualCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"Skyrim placeholder mismatch for {context}: {key} (expected {expectedCount}, got {actualCount})."
                );
            }
        }
    }

    private static Dictionary<string, int> CountRawSkyrimSemanticPlaceholders(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in RawSkyrimSemanticPlaceholderRegex.Matches(text))
        {
            if (!m.Success)
            {
                continue;
            }

            var sign = m.Groups["sign"].Value;
            var kind = m.Groups["kind"].Value.Trim().ToLowerInvariant();
            if (kind.Length == 0)
            {
                continue;
            }

            var hasPct = m.Groups["pct"].Success && !string.IsNullOrWhiteSpace(m.Groups["pct"].Value);
            var key = sign + "<" + kind + ">" + (hasPct ? "%" : "");

            if (counts.TryGetValue(key, out var n))
            {
                counts[key] = n + 1;
            }
            else
            {
                counts[key] = 1;
            }
        }

        return counts;
    }

    private static void ValidateTokensPreservedSameCount(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string context)
    {
        if (IsMovablePlaceholderReorderAllowed(expected, actual))
        {
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                ThrowTokenSequenceMismatch(expected, actual, context);
            }
        }
    }

    private static bool IsMovablePlaceholderReorderAllowed(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var expectedCounts = CountTokens(expected);
        var actualCounts = CountTokens(actual);
        return AreTokenCountsEqual(expectedCounts, actualCounts)
            && IsTokenOrderCompatibleWithMovablePlaceholders(expected, actual);
    }

    private static void ThrowTokenSequenceMismatch(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string context)
    {
        var firstMismatch = FindFirstTokenMismatchIndex(expected, actual);
        if (firstMismatch < 0)
        {
            firstMismatch = 0;
        }

        throw new InvalidOperationException(
            $"Token sequence mismatch for {context} at index {firstMismatch}: expected {expected[firstMismatch]}, got {actual[firstMismatch]}."
        );
    }

    private static void ValidateTokensPreservedDifferentCount(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string context)
    {
        var expectedCounts = CountTokens(expected);
        var actualCounts = CountTokens(actual);

        foreach (var (token, expectedCount) in expectedCounts)
        {
            actualCounts.TryGetValue(token, out var actualCount);
            if (actualCount < expectedCount)
            {
                throw new InvalidOperationException(
                    $"Missing token in translation for {context}: {token} (expected {expectedCount}, got {actualCount})."
                );
            }
        }

        foreach (var (token, actualCount) in actualCounts)
        {
            expectedCounts.TryGetValue(token, out var expectedCount);
            if (actualCount > expectedCount)
            {
                throw new InvalidOperationException(
                    $"Unexpected token in translation for {context}: {token} (expected {expectedCount}, got {actualCount})."
                );
            }
        }

        throw new InvalidOperationException(
            $"Token count mismatch for {context}: expected {expected.Count} tokens, got {actual.Count} tokens."
        );
    }

    private static int FindFirstTokenMismatchIndex(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var n = Math.Min(expected.Count, actual.Count);
        for (var i = 0; i < n; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool AreTokenCountsEqual(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (token, countA) in a)
        {
            if (!b.TryGetValue(token, out var countB) || countA != countB)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTokenOrderCompatibleWithMovablePlaceholders(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var expectedFixed = new List<string>();
        var expectedMovableSegments = new List<Dictionary<string, int>>();
        BuildFixedAndMovableSegments(expected, expectedFixed, expectedMovableSegments);

        var actualFixed = new List<string>();
        var actualMovableSegments = new List<Dictionary<string, int>>();
        BuildFixedAndMovableSegments(actual, actualFixed, actualMovableSegments);

        if (expectedFixed.Count != actualFixed.Count)
        {
            return false;
        }

        for (var i = 0; i < expectedFixed.Count; i++)
        {
            if (!string.Equals(expectedFixed[i], actualFixed[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (expectedMovableSegments.Count != actualMovableSegments.Count)
        {
            return false;
        }

        for (var i = 0; i < expectedMovableSegments.Count; i++)
        {
            if (!AreTokenCountsEqual(expectedMovableSegments[i], actualMovableSegments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void BuildFixedAndMovableSegments(
        IReadOnlyList<string> tokens,
        List<string> fixedTokens,
        List<Dictionary<string, int>> movableSegments
    )
    {
        var current = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (IsMovablePlaceholderToken(token))
            {
                if (current.TryGetValue(token, out var n))
                {
                    current[token] = n + 1;
                }
                else
                {
                    current[token] = 1;
                }
                continue;
            }

            movableSegments.Add(current);
            current = new Dictionary<string, int>(StringComparer.Ordinal);
            fixedTokens.Add(token);
        }

        movableSegments.Add(current);
    }

    private static bool IsMovablePlaceholderToken(string token)
        => token.StartsWith("__XT_PH_MAG_", StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_NUM_", StringComparison.Ordinal);

    // ── Repair helpers ──

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
            return TokenMismatchDecision.DropOutputToken;
        }

        if (nextExpHasOutput && !nextOutHasExpected)
        {
            return TokenMismatchDecision.InsertExpectedToken;
        }

        if (nextOutHasExpected && nextExpHasOutput)
        {
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
