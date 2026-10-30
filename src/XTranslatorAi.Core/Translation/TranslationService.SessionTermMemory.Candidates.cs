using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private IReadOnlyList<(string Source, string Target)> MergeSessionPromptOnlyGlossaryForText(
        string text,
        IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary
    )
    {
        if (!Ctx.EnableSessionTermMemory || Ctx.SessionTermMemory == null)
        {
            return AddRepeatedSentencePairs(new[] { text }, basePromptOnlyGlossary);
        }

        return AddRepeatedSentencePairs(new[] { text }, Ctx.SessionTermMemory.MergeForText(text, basePromptOnlyGlossary));
    }

    private IReadOnlyList<(string Source, string Target)> MergeSessionPromptOnlyGlossaryForTexts(
        IReadOnlyList<string> texts,
        IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary
    )
    {
        if (!Ctx.EnableSessionTermMemory || Ctx.SessionTermMemory == null)
        {
            return AddRepeatedSentencePairs(texts, basePromptOnlyGlossary);
        }

        return AddRepeatedSentencePairs(texts, Ctx.SessionTermMemory.MergeForTexts(texts, basePromptOnlyGlossary));
    }

    private IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> SelectSessionTermSeedItems(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        int maxSeedCount
    )
    {
        if (!Ctx.EnableSessionTermMemory || maxSeedCount <= 0)
        {
            return Array.Empty<(long, string, string, MaskedText, GlossaryApplication)>();
        }

        // Pick definition rows (FULL/NAM/TITLE) that correspond to term candidates.
        var definitionByKey =
            new Dictionary<string, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var it in items)
        {
            if (!SessionTermRules.IsTermRec(GetRecForId(it.Id)))
            {
                continue;
            }

            if (!IsAutomaticSessionTermCandidate(it.Source))
            {
                continue;
            }

            // Seeding is only helpful when the definition row is short enough to complete quickly.
            if (it.Masked.Length > 6000)
            {
                continue;
            }

            var key = SessionTermRules.NormalizeKey(it.Source);
            if (!definitionByKey.ContainsKey(key))
            {
                definitionByKey[key] = it;
            }
        }

        if (definitionByKey.Count == 0)
        {
            return Array.Empty<(long, string, string, MaskedText, GlossaryApplication)>();
        }

        var definitionKeys = new HashSet<string>(definitionByKey.Keys, StringComparer.OrdinalIgnoreCase);

        // Count title-like English phrases/words across all sources, but only when we have a definition row for it.
        var termCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Source))
            {
                continue;
            }

            foreach (Match m in SessionTermRules.EnglishPhrase.Matches(it.Source))
            {
                var key = SessionTermRules.NormalizeKey(m.Value);
                if (key.Length is < 3 or > 60 || !definitionKeys.Contains(key))
                {
                    continue;
                }

                if (termCounts.TryGetValue(key, out var count))
                {
                    termCounts[key] = count + 1;
                }
                else
                {
                    termCounts[key] = 1;
                }
            }

            foreach (Match m in SessionTermRules.EnglishTitleWord.Matches(it.Source))
            {
                var key = SessionTermRules.NormalizeKey(m.Value);
                if (key.Length is < 3 or > 60 || !definitionKeys.Contains(key))
                {
                    continue;
                }

                if (termCounts.TryGetValue(key, out var count))
                {
                    termCounts[key] = count + 1;
                }
                else
                {
                    termCounts[key] = 1;
                }
            }
        }

        var candidates = new List<(int Count, int Len, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) Item)>();
        foreach (var (key, count) in termCounts)
        {
            if (count < 2)
            {
                continue;
            }

            if (definitionByKey.TryGetValue(key, out var def))
            {
                candidates.Add((Count: count, Len: key.Length, Item: def));
            }
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<(long, string, string, MaskedText, GlossaryApplication)>();
        }

        candidates.Sort((a, b) =>
        {
            var byCount = b.Count.CompareTo(a.Count);
            if (byCount != 0)
            {
                return byCount;
            }
            return b.Len.CompareTo(a.Len);
        });

        var take = Math.Min(maxSeedCount, candidates.Count);
        var result = new (long, string, string, MaskedText, GlossaryApplication)[take];
        for (var i = 0; i < take; i++)
        {
            result[i] = candidates[i].Item;
        }

        return result;
    }
}
