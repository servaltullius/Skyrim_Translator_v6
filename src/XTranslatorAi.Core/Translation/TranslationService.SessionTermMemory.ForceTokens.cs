using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) PrepareRowWithSessionTermForceTokens(
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row
    )
    {
        if (!_enableSessionTermMemory || _sessionTermMemory == null)
        {
            return row;
        }

        if (string.IsNullOrWhiteSpace(row.Masked) || _sessionTermMemory.IsEmpty)
        {
            return row;
        }

        var excludedSources = SessionTermMemory.BuildExcludedSources(row.Glossary.PromptOnlyPairs);
        var forcingEntries = _sessionTermMemory.GetForcingEntriesForText(row.Masked, excludedSources);
        if (forcingEntries.Count == 0)
        {
            return row;
        }

        var masked = ReplaceSessionTermsTokenSafe(row.Masked, forcingEntries, out var usedTokenToReplacement);
        if (ReferenceEquals(masked, row.Masked) || string.Equals(masked, row.Masked, StringComparison.Ordinal))
        {
            return row;
        }

        var mergedTokenToReplacement = MergeTokenReplacementMaps(row.Glossary.TokenToReplacement, usedTokenToReplacement);
        var mergedGlossary = new GlossaryApplication(masked, mergedTokenToReplacement, row.Glossary.PromptOnlyPairs);
        return (row.Id, row.Source, masked, row.Mask, mergedGlossary);
    }

    private IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> PrepareBatchWithSessionTermForceTokens(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        if (!_enableSessionTermMemory || _sessionTermMemory == null || _sessionTermMemory.IsEmpty)
        {
            return batch;
        }

        if (batch.Count == 0)
        {
            return batch;
        }

        var anyChanged = false;
        var prepared = new (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            prepared[i] = PrepareRowWithSessionTermForceTokens(batch[i]);
            anyChanged |= !string.Equals(prepared[i].Masked, batch[i].Masked, StringComparison.Ordinal);
        }

        return anyChanged ? prepared : batch;
    }

    private static IReadOnlyDictionary<string, string> MergeTokenReplacementMaps(
        IReadOnlyDictionary<string, string> baseTokenToReplacement,
        IReadOnlyDictionary<string, string> extraTokenToReplacement
    )
    {
        if (extraTokenToReplacement.Count == 0)
        {
            return baseTokenToReplacement;
        }

        if (baseTokenToReplacement.Count == 0)
        {
            return extraTokenToReplacement;
        }

        var merged = new Dictionary<string, string>(capacity: baseTokenToReplacement.Count + extraTokenToReplacement.Count, comparer: StringComparer.Ordinal);
        foreach (var (k, v) in baseTokenToReplacement)
        {
            merged[k] = v;
        }
        foreach (var (k, v) in extraTokenToReplacement)
        {
            merged[k] = v;
        }
        return merged;
    }

    private static string ReplaceSessionTermsTokenSafe(
        string input,
        IReadOnlyList<(string Source, string Token, string Target)> forcingEntries,
        out IReadOnlyDictionary<string, string> usedTokenToReplacement
    )
    {
        usedTokenToReplacement = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(input) || forcingEntries.Count == 0)
        {
            return input;
        }

        SplitByTokens(input, out var texts, out var tokens);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = ApplySessionTermReplacements(texts, forcingEntries, used);

        if (!changed)
        {
            return input;
        }

        usedTokenToReplacement = used;
        return JoinTextAndTokens(texts, tokens);
    }

    private static bool ApplySessionTermReplacements(
        List<string> texts,
        IReadOnlyList<(string Source, string Token, string Target)> forcingEntries,
        Dictionary<string, string> usedTokenToReplacement
    )
    {
        var changed = false;
        foreach (var (source, token, target) in forcingEntries)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var isSingleWord = source.IndexOf(' ') < 0;

            // Prefer removing leading articles when present ("The X" / "A X" / "An X").
            // This keeps title/name phrases cleaner in Korean.
            var the = "The " + source;
            var an = "An " + source;
            var a = "A " + source;

            var pattern = new SessionTermReplacementPattern(
                Source: source,
                Token: token,
                IsSingleWord: isSingleWord,
                The: the,
                An: an,
                A: a
            );

            if (ApplySessionTermReplacementToSegments(texts, pattern))
            {
                usedTokenToReplacement[token] = target;
                changed = true;
            }
        }

        return changed;
    }

    private readonly record struct SessionTermReplacementPattern(
        string Source,
        string Token,
        bool IsSingleWord,
        string The,
        string An,
        string A
    );

    private static bool ApplySessionTermReplacementToSegments(
        List<string> texts,
        SessionTermReplacementPattern pattern
    )
    {
        var any = false;
        for (var i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            if (string.IsNullOrEmpty(t))
            {
                continue;
            }

            var replaced = ReplaceSessionTermInSegment(t, pattern);
            if (!string.Equals(replaced, t, StringComparison.Ordinal))
            {
                texts[i] = replaced;
                any = true;
            }
        }

        return any;
    }

    private static string ReplaceSessionTermInSegment(string text, SessionTermReplacementPattern pattern)
    {
        var replaced = ReplaceSubstringOrdinalIgnoreCase(text, pattern.The, pattern.Token);
        replaced = ReplaceSubstringOrdinalIgnoreCase(replaced, pattern.An, pattern.Token);
        replaced = ReplaceSubstringOrdinalIgnoreCase(replaced, pattern.A, pattern.Token);
        return pattern.IsSingleWord
            ? ReplaceWholeAsciiWordOrdinalIgnoreCase(replaced, pattern.Source, pattern.Token)
            : ReplaceSubstringOrdinalIgnoreCase(replaced, pattern.Source, pattern.Token);
    }

    private static string ReplaceWholeAsciiWordOrdinalIgnoreCase(string input, string sourceTerm, string replacement)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(sourceTerm))
        {
            return input;
        }

        var first = input.IndexOf(sourceTerm, StringComparison.OrdinalIgnoreCase);
        if (first < 0)
        {
            return input;
        }

        var sb = new System.Text.StringBuilder(capacity: input.Length);
        var cursor = 0;
        while (cursor < input.Length)
        {
            var next = input.IndexOf(sourceTerm, cursor, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
            {
                sb.Append(input.AsSpan(cursor));
                break;
            }

            sb.Append(input.AsSpan(cursor, next - cursor));

            var beforeOk = next == 0 || !IsAsciiWordChar(input[next - 1]);
            var afterPos = next + sourceTerm.Length;
            var afterOk = afterPos >= input.Length || !IsAsciiWordChar(input[afterPos]);
            if (beforeOk && afterOk)
            {
                sb.Append(replacement);
            }
            else
            {
                sb.Append(input.AsSpan(next, sourceTerm.Length));
            }

            cursor = next + sourceTerm.Length;
        }

        return sb.ToString();
    }

    private static bool IsAsciiWordChar(char ch)
        => (ch is >= 'A' and <= 'Z')
           || (ch is >= 'a' and <= 'z')
           || (ch is >= '0' and <= '9')
           || ch == '_';

    private static string ReplaceSubstringOrdinalIgnoreCase(string input, string sourceTerm, string replacement)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(sourceTerm))
        {
            return input;
        }

        var first = input.IndexOf(sourceTerm, StringComparison.OrdinalIgnoreCase);
        if (first < 0)
        {
            return input;
        }

        var sb = new System.Text.StringBuilder(capacity: input.Length);
        sb.Append(input.AsSpan(0, first));
        sb.Append(replacement);

        var cursor = first + sourceTerm.Length;
        while (cursor < input.Length)
        {
            var next = input.IndexOf(sourceTerm, cursor, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
            {
                sb.Append(input.AsSpan(cursor));
                break;
            }

            sb.Append(input.AsSpan(cursor, next - cursor));
            sb.Append(replacement);
            cursor = next + sourceTerm.Length;
        }

        return sb.ToString();
    }
}

