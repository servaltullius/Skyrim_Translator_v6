using System;
using System.Collections.Generic;
using System.Linq;
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
        var actual = ExtractTokens(outputText);
        var expected = WithoutDroppedPronouns(ExtractTokens(inputText), actual);

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
            if (actualCount != expectedCount && !IsDroppedPronoun(tag, expectedCount, actualCount))
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
        ValidatePlainPercentValues(sourceText, finalText, expected, actual, context);
        foreach (var (placeholder, count) in expected)
        {
            actual.TryGetValue(placeholder, out var actualCount);
            if (count != actualCount && !IsDroppedPronoun(placeholder, count, actualCount))
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
        var formattingNames = ProtectedTextKinds.FormattingNamesFor(sourceProtected);
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

    private static readonly Regex PlainPercentPlaceholderRegex = new(
        pattern: @"^(?<n>[+-]?\d+(?:\.\d+)?)[\t ]*%$",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex PercentWordRegex = new(
        pattern: @"(?<![\w.])(?<n>[+-]?\d+(?:\.\d+)?)[\t ]*(?:percent|per[\t ]?cent|퍼센트)(?![A-Za-z])",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// "50 percent", "50%" and "50퍼센트" state the same value, and translations naturally switch
    /// between them. Plain percentages are therefore compared by value and count, not by spelling,
    /// and removed from the exact protected-text comparison. A changed or missing value still fails.
    /// </summary>
    private static void ValidatePlainPercentValues(
        string sourceText,
        string finalText,
        Dictionary<string, int> expected,
        Dictionary<string, int> actual,
        string context
    )
    {
        var expectedValues = TakePlainPercentValues(expected, sourceText);
        var actualValues = TakePlainPercentValues(actual, finalText);
        foreach (var value in expectedValues.Keys.Union(actualValues.Keys))
        {
            expectedValues.TryGetValue(value, out var expectedCount);
            actualValues.TryGetValue(value, out var actualCount);
            if (expectedCount != actualCount)
            {
                throw new InvalidOperationException(
                    $"Protected text mismatch for {context}: {value}% (expected {expectedCount}, got {actualCount})."
                );
            }
        }
    }

    private static Dictionary<string, int> TakePlainPercentValues(Dictionary<string, int> protectedCounts, string text)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var placeholder in protectedCounts.Keys.ToList())
        {
            var m = PlainPercentPlaceholderRegex.Match(placeholder);
            if (!m.Success)
            {
                continue;
            }

            Add(values, NormalizePercentValue(m.Groups["n"].Value), protectedCounts[placeholder]);
            protectedCounts.Remove(placeholder);
        }

        foreach (Match m in PercentWordRegex.Matches(text))
        {
            Add(values, NormalizePercentValue(m.Groups["n"].Value), 1);
        }

        foreach (Match m in SpelledPercentRegex.Matches(text))
        {
            Add(values, SpelledNumber(m.Groups["n"].Value).ToString(System.Globalization.CultureInfo.InvariantCulture), 1);
        }

        return values;

        static void Add(Dictionary<string, int> counts, string key, int n)
            => counts[key] = counts.TryGetValue(key, out var existing) ? existing + n : n;
    }

    private static readonly string[] Units =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve",
        "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens = { "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

    // Feris spells the number too: "Seventy percent now." is "이제 70%야." in Korean.
    private static readonly Regex SpelledPercentRegex = new(
        pattern: @"(?<![A-Za-z\-])(?<n>(?:a|one)[\t -]+hundred|(?:" + string.Join("|", Tens) + @")(?:[\t -]+(?:"
                 + string.Join("|", Units.Skip(1).Take(9)) + @"))?|" + string.Join("|", Units.Reverse()) + @")[\t ]*(?:percent|per[\t ]?cent)(?![A-Za-z])",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static int SpelledNumber(string words)
    {
        var parts = words.ToLowerInvariant().Split(new[] { ' ', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts[^1] == "hundred")
        {
            return 100;
        }

        var tens = Array.IndexOf(Tens, parts[0]);
        return tens >= 0
            ? (tens + 2) * 10 + (parts.Length > 1 ? Array.IndexOf(Units, parts[1]) : 0)
            : Array.IndexOf(Units, parts[0]);
    }

    // "+10%" in a stat line is usually written "10% 증가" in Korean; the sign moves into the verb.
    private static string NormalizePercentValue(string value) => value.TrimStart('+');

    private static List<string> ExtractFixedProtectedText(IEnumerable<string> placeholders, ISet<string> formattingNames)
    {
        var structure = new List<string>();
        foreach (var placeholder in placeholders)
        {
            if (ProtectedTextKinds.IsLayout(placeholder, formattingNames))
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

        ValidateLinesAligned(inputText, outputText, context);

        // Between layout tokens only: a name or a value (__XT_TERM_, __XT_PH_VAR_ …) follows Korean word order, and cut
        // at those a long sentence whose name moved to the front had an empty Korean "segment" and was refused as an
        // omission, again on every retry (a 5,093-letter book ended at the call limit). The tokens' own letters are not
        // the text's.
        var inputTexts = LayoutTokenRegex.Split(inputText);
        var outputTexts = LayoutTokenRegex.Split(outputText);

        if (inputTexts.Length != outputTexts.Length)
        {
            return;
        }

        for (var i = 0; i < inputTexts.Length; i++)
        {
            var inLen = CountLettersOrDigits(TranslationConstants.XtTokenRegex.Replace(inputTexts[i], " "));
            if (inLen < 240)
            {
                continue;
            }

            var outLen = CountLettersOrDigits(TranslationConstants.XtTokenRegex.Replace(outputTexts[i], " "));

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

    private static readonly Regex LayoutTokenRegex = new(
        pattern: @"__XT_PH_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RawAngleTagRegex = new(
        pattern: @"<" + TranslationConstants.StageDirectionGuard + @"[^>]*>",
        options: RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Plain __XT_PH_####__ tokens are layout (line breaks, page breaks, formatting tags). A slot between
    /// them that is empty in the source must stay empty; text there means the model shifted lines past
    /// the layout (a duplicated line pushing a poem past "&lt;/p&gt;" and the page breaks). The reverse is
    /// allowed: Korean word order often joins a title split over two lines ("A Short History / of Morrowind").
    /// </summary>
    private static void ValidateLinesAligned(string inputText, string outputText, string context)
    {
        var inputSlots = LayoutTokenRegex.Split(inputText);
        var outputSlots = LayoutTokenRegex.Split(outputText);
        if (inputSlots.Length != outputSlots.Length)
        {
            return; // Token validation reports layout count differences.
        }

        // One moved line is cosmetic ("<font>" and the first sentence joined on one line) and not worth
        // losing the whole translation over. A shift cascades into many slots (8–18 in the evaluation).
        const int shiftedSlotLimit = 3;
        var shifted = 0;
        for (var i = 0; i < inputSlots.Length; i++)
        {
            if (!HasContent(inputSlots[i]) && HasContent(outputSlots[i]) && ++shifted >= shiftedSlotLimit)
            {
                throw new InvalidOperationException(
                    $"Translation shifted text across line breaks or tags for {context} (slot {i})."
                );
            }
        }
    }

    private static bool HasContent(string slot)
    {
        if (TranslationConstants.XtTokenRegex.IsMatch(slot))
        {
            return true; // A term or value.
        }

        foreach (var ch in RawAngleTagRegex.Replace(slot, ""))
        {
            if (char.IsLetter(ch))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Before <see cref="TryRepairTokens"/> is taken: a model that left out a line together with its line break had the
    /// break put back at the end by the repair, and the line passed as two title lines joined ("Costs 50 magicka and
    /// staggers the caster." was gone from a spell's description, saved as Done). By the layout tokens the answer kept,
    /// the source lines that came out as one line may be a title and its fragment ("A Short History / of Morrowind"),
    /// not two sentences (one ends in . ! ? or is 20 letters long), and must keep some of their letters ("작자 미상"
    /// alone for a sentence and "by Anonymous" was a sentence dropped in the middle). An answer that kept every layout
    /// token is not judged: a name before a tag empties a slot in Korean word order, and the slots are the source's. A
    /// token the answer repeats (or the source does not have) is dropped by the repair, and its line runs on: counted
    /// tokens let a line dropped beside a repeated break pass.
    /// </summary>
    internal static void ValidateRepairKeptLines(string inputText, string outputText, string context)
    {
        var inputTokens = LayoutTokenRegex.Matches(inputText).Select(m => m.Value).ToList();
        var outputTokens = LayoutTokenRegex.Matches(outputText).Select(m => m.Value).ToList();
        if (!inputTokens.Except(outputTokens, StringComparer.Ordinal).Any())
        {
            return;
        }

        // Each output line with the source lines it stands for: from after the kept token before it to the kept token
        // after it. An answer with its tokens out of the source's order is not judged here.
        var inputSlots = LayoutTokenRegex.Split(inputText);
        var outputSlots = LayoutTokenRegex.Split(outputText);
        var lines = new List<(string Text, int First, int Last)>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var (text, first) = (outputSlots[0], 0);
        for (var t = 0; t < outputTokens.Count; t++)
        {
            var at = used.Add(outputTokens[t]) ? inputTokens.IndexOf(outputTokens[t]) : -1;
            if (at < 0)
            {
                text += " " + outputSlots[t + 1];
                continue;
            }

            if (at < first)
            {
                return;
            }

            lines.Add((text, first, at));
            (text, first) = (outputSlots[t + 1], at + 1);
        }

        lines.Add((text, first, inputSlots.Length - 1));
        foreach (var line in lines.Where(line => line.Last > line.First))
        {
            var source = inputSlots[line.First..(line.Last + 1)];
            var letters = source.Sum(Letters);
            if (source.Count(IsSentence) >= 2 || letters >= 4 && Letters(line.Text) < Math.Ceiling(letters * 0.15))
            {
                throw new InvalidOperationException($"Translation dropped a line for {context} (lines {line.First}-{line.Last} came out as one).");
            }
        }

        static string Plain(string slot) => RawAngleTagRegex.Replace(TranslationConstants.XtTokenRegex.Replace(slot, " "), " ").Trim();

        static int Letters(string slot) => CountLettersOrDigits(Plain(slot));

        static bool IsSentence(string slot)
        {
            var text = Plain(slot);
            var letters = CountLettersOrDigits(text);
            return letters >= 20 || letters >= 4 && text[^1] is '.' or '!' or '?' or '。';
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
            repaired = RepairSameCountTokens(expected, outputText, actual);
            return true;
        }

        repaired = RepairTokenCountMismatchGreedy(inputText, expected, outputText, actual, glossaryTokenToReplacement);
        return repaired.Length > 0;
    }

    /// <summary>
    /// Keeps every movable token the model placed (terms and values may follow Korean word order)
    /// and reassigns only the remaining slots, in source order, so layout tokens regain their order.
    /// </summary>
    private static string RepairSameCountTokens(IReadOnlyList<string> expected, string outputText, IReadOnlyList<string> actual)
    {
        var unplaced = CountTokens(expected);
        var keep = new bool[actual.Count];
        for (var i = 0; i < actual.Count; i++)
        {
            if (IsMovablePlaceholderToken(actual[i]) && unplaced.TryGetValue(actual[i], out var n) && n > 0)
            {
                keep[i] = true;
                unplaced[actual[i]] = n - 1;
            }
        }

        var fill = new Queue<string>();
        foreach (var token in expected)
        {
            if (unplaced.TryGetValue(token, out var n) && n > 0)
            {
                fill.Enqueue(token);
                unplaced[token] = n - 1;
            }
        }

        var index = 0;
        return TranslationConstants.XtTokenRegex.Replace(
            outputText,
            m => keep[index++] ? m.Value : fill.Dequeue()
        );
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
        // Terms and runtime values may also cross line breaks and tags: prose wrapped mid-sentence
        // is reordered across the break. Numeric values stay within their line (stat text).
        var expectedFixed = new List<string>();
        var expectedMovableSegments = new List<Dictionary<string, int>>();
        BuildFixedAndMovableSegments(WithoutFreeTokens(expected), expectedFixed, expectedMovableSegments);

        var actualFixed = new List<string>();
        var actualMovableSegments = new List<Dictionary<string, int>>();
        BuildFixedAndMovableSegments(WithoutFreeTokens(actual), actualFixed, actualMovableSegments);

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

    private const string PronounTokenPrefix = "__XT_PH_PRON_";

    // A pronoun token the translation left out is not missing (see PlaceholderMasker.IsPronounAlias); one it repeats or
    // invents still fails.
    private static List<string> WithoutDroppedPronouns(List<string> expected, IReadOnlyList<string> actual)
    {
        if (!expected.Any(token => token.StartsWith(PronounTokenPrefix, StringComparison.Ordinal)))
        {
            return expected;
        }

        var kept = CountTokens(actual);
        var result = new List<string>(expected.Count);
        foreach (var token in expected)
        {
            if (!token.StartsWith(PronounTokenPrefix, StringComparison.Ordinal))
            {
                result.Add(token);
            }
            else if (kept.TryGetValue(token, out var n) && n > 0)
            {
                kept[token] = n - 1;
                result.Add(token);
            }
        }

        return result;
    }

    // The same for the raw tags of a finished text: fewer pronoun aliases than the source is fine.
    private static bool IsDroppedPronoun(string original, int expectedCount, int actualCount)
        => actualCount < expectedCount && XTranslatorAi.Core.Text.PlaceholderMasker.IsPronounAlias(original);

    private static bool IsFreeToken(string token)
        => token.StartsWith("__XT_PH_VAR_", StringComparison.Ordinal)
            || token.StartsWith(PronounTokenPrefix, StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_KEY_", StringComparison.Ordinal)
            || token.StartsWith("__XT_TERM_", StringComparison.Ordinal);

    private static List<string> WithoutFreeTokens(IReadOnlyList<string> tokens)
    {
        var result = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            if (!IsFreeToken(token))
            {
                result.Add(token);
            }
        }
        return result;
    }

    // Values and glossary terms follow target-language word order ("the Jarl of Whiterun" →
    // "화이트런의 야를"). Plain __XT_PH_####__ tokens are layout (line breaks, formatting tags).
    private static bool IsMovablePlaceholderToken(string token)
        => token.StartsWith("__XT_PH_MAG_", StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_NUM_", StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_VAR_", StringComparison.Ordinal)
            || token.StartsWith(PronounTokenPrefix, StringComparison.Ordinal)
            || token.StartsWith("__XT_PH_KEY_", StringComparison.Ordinal)
            || token.StartsWith("__XT_TERM_", StringComparison.Ordinal);

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

        if (!RepairMovableTokens(expectedTokens, texts, tokens, glossaryTokenToReplacement))
        {
            return "";
        }

        // Layout tokens must keep source order: align them greedily, ignoring movable tokens.
        var expectedFixed = new List<string>();
        foreach (var token in expectedTokens)
        {
            if (!IsMovablePlaceholderToken(token))
            {
                expectedFixed.Add(token);
            }
        }

        var fixedSlots = new List<int>();
        var outputFixed = new List<string>();
        for (var j = 0; j < tokens.Count; j++)
        {
            if (tokens[j].Length > 0 && !IsMovablePlaceholderToken(tokens[j]) && TranslationConstants.XtTokenRegex.IsMatch(tokens[j]))
            {
                fixedSlots.Add(j);
                outputFixed.Add(tokens[j]);
            }
        }

        var iExp = 0;
        var jOut = 0;
        const int lookahead = 8;

        while (iExp < expectedFixed.Count && jOut < outputFixed.Count)
        {
            if (string.Equals(outputFixed[jOut], expectedFixed[iExp], StringComparison.Ordinal))
            {
                iExp++;
                jOut++;
                continue;
            }

            switch (DecideTokenMismatch(expectedFixed, outputFixed, iExp, jOut, lookahead))
            {
                case TokenMismatchDecision.DropOutputToken:
                    tokens[fixedSlots[jOut]] = "";
                    jOut++;
                    break;
                case TokenMismatchDecision.InsertExpectedToken:
                    InsertTokenAtTextBoundary(inputText, texts, fixedSlots[jOut], expectedFixed[iExp], glossaryTokenToReplacement);
                    iExp++;
                    break;
                default:
                    tokens[fixedSlots[jOut]] = expectedFixed[iExp];
                    iExp++;
                    jOut++;
                    break;
            }
        }

        while (jOut < outputFixed.Count)
        {
            tokens[fixedSlots[jOut]] = "";
            jOut++;
        }

        var ctx = new TokenRepairContext(inputText, expectedFixed, tokens, texts, glossaryTokenToReplacement);
        return FinalizeTokenRepair(ctx, iExp, tokens.Count);
    }

    /// <summary>
    /// Matches glossary terms and runtime values by count, not position. Surplus copies of a term
    /// become its plain translation; a missing term takes the place of its translation if the model
    /// wrote that instead. A missing term or value with no such place fails the repair rather than
    /// being appended to the end of the text.
    /// </summary>
    private static bool RepairMovableTokens(
        IReadOnlyList<string> expectedTokens,
        List<string> texts,
        List<string> tokens,
        IReadOnlyDictionary<string, string>? glossaryTokenToReplacement
    )
    {
        var unplaced = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in expectedTokens)
        {
            if (IsMovablePlaceholderToken(token))
            {
                unplaced[token] = unplaced.TryGetValue(token, out var n) ? n + 1 : 1;
            }
        }

        for (var j = 0; j < tokens.Count; j++)
        {
            var token = tokens[j];
            if (!IsMovablePlaceholderToken(token))
            {
                continue;
            }

            if (unplaced.TryGetValue(token, out var n) && n > 0)
            {
                unplaced[token] = n - 1;
                continue;
            }

            tokens[j] = glossaryTokenToReplacement != null
                        && glossaryTokenToReplacement.TryGetValue(token, out var replacement)
                ? replacement
                : "";
        }

        foreach (var (token, missing) in unplaced)
        {
            for (var k = 0; k < missing; k++)
            {
                // Not for one syllable: the 은 of "그들은" is not the material 은 (Silver), as in ApplyTokensAndUnmask.
                if (glossaryTokenToReplacement == null
                    || !token.StartsWith("__XT_TERM_", StringComparison.Ordinal)
                    || !glossaryTokenToReplacement.TryGetValue(token, out var replacement)
                    || replacement.Trim().Length <= 1
                    || !TryReplaceFirstOccurrenceInAnyText(texts, replacement, token))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryReplaceFirstOccurrenceInAnyText(List<string> texts, string needle, string replacement)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            if (TryReplaceFirstOccurrence(texts, i, needle, replacement))
            {
                return true;
            }
        }
        return false;
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
