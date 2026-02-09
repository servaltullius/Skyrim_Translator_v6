using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static readonly Regex RawSkyrimSemanticPlaceholderRegex = new(
        pattern: @"(?<sign>[+-]?)<\s*(?<kind>mag|dur|bur)\s*>(?<pct>\s*%)?",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static void ValidateTokensPreserved(string inputText, string outputText, string context)
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

    private static void ValidateRawTagsPreserved(string inputText, string outputText, string context)
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
        foreach (Match m in RawMarkupTagRegex.Matches(inputText))
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
            // Input doesn't contain any raw tags that need preservation.
            return;
        }

        var actualCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in RawMarkupTagRegex.Matches(outputText))
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

    private static void ValidateFinalTextIntegrity(string sourceText, string finalText, string context)
    {
        // After post-edits (template fixes, unit binding, Korean fixes), we re-validate that:
        // - no internal __XT_* tokens leaked into the final output
        // - raw tags/placeholders that are part of the runtime contract are still preserved
        ValidateTokensPreserved(sourceText, finalText, context);
        ValidateRawTagsPreserved(sourceText, finalText, context);
    }

    private static void ValidateRawSkyrimSemanticPlaceholdersPreserved(string inputText, string outputText, string context)
    {
        if (string.IsNullOrWhiteSpace(inputText) || string.IsNullOrWhiteSpace(outputText))
        {
            return;
        }

        // Only run this for strings that actually contain these placeholders.
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
            var kind = (m.Groups["kind"].Value ?? "").Trim().ToLowerInvariant();
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
        // Allow reordering ONLY for well-known semantic placeholders (e.g., <mag>/<dur>) so translators can produce
        // natural word order in the target language. Formatting/markup/glossary tokens must keep their order.
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
}
