using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Terms that recur inside names but never stand alone as a row. Session term memory learns a term only from a row
/// whose whole source is that term, so "Deathblow" (in "Parry - Deathblow Paralyzed", "Elden Deathblow", and 91 rows in
/// all) came out in seven spellings (치명타, 데스블로우, 결정타, …) and "Rebreath" in about twelve. Before the run,
/// the parts of name rows that recur in at least four rows are translated once, as their own small batch, and the
/// results are offered to later batches as reference terms (not forced, like other automatic session terms).
/// </summary>
public sealed partial class TranslationService
{
    internal const int MaxSubtermSeeds = 24;
    private const int MinSubtermRows = 4;

    private static readonly System.Text.RegularExpressions.Regex LowercaseWordRegex = new(@"\b[a-z][a-z'’\-]+\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly string[] NamePartSeparators = { " - ", " – ", ": ", " (", ")" };

    private const string SubtermStyleHint =
        "A term that recurs inside this mod's skill, effect and item names. Translate it as a short Korean noun phrase, "
        + "the way it should read every time inside those names and descriptions.";

    internal static IReadOnlyList<string> SelectSubtermSeeds(
        IReadOnlyList<(string Source, string Rec, string Masked)> rows,
        Func<string, bool> isKnownTerm,
        int max)
    {
        var parts = new HashSet<string>(StringComparer.Ordinal);
        var wholeRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var source = (row.Source ?? "").Trim();
            wholeRows.Add(source);
            if (!IsSessionTermRec(row.Rec))
            {
                continue;
            }

            foreach (var part in source.Split(NamePartSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var phrase in EnglishPhraseRegex.Matches(part).Select(m => m.Value)
                             .Concat(EnglishTitleWordRegex.Matches(part).Select(m => m.Value)))
                {
                    var key = NormalizeSessionTermKey(phrase);
                    if (key.Length >= 4 && IsAutomaticSessionTermCandidate(key))
                    {
                        parts.Add(key);
                    }
                }
            }
        }

        // A single word the sources also write in lower case is an ordinary word ("an ancient sword"), not a term.
        var lowercaseWords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (System.Text.RegularExpressions.Match m in LowercaseWordRegex.Matches(row.Source ?? ""))
            {
                lowercaseWords.Add(m.Value);
            }
        }

        var counted = new List<(string Term, int Rows)>();
        foreach (var term in parts)
        {
            // A whole row is already a definition the regular seeding translates; a known term needs nothing.
            if (wholeRows.Contains(term) || isKnownTerm(term)
                || (!term.Contains(' ') && lowercaseWords.Contains(term.ToLowerInvariant())))
            {
                continue;
            }

            var count = 0;
            var stillPlain = false;
            foreach (var row in rows)
            {
                if (!ContainsSessionTerm(row.Source ?? "", term))
                {
                    continue;
                }

                count++;
                // The glossary already turned it into a token everywhere: it has its translation.
                stillPlain |= ContainsSessionTerm(row.Masked ?? "", term);
            }

            if (count >= MinSubtermRows && stillPlain)
            {
                counted.Add((term, count));
            }
        }

        // Prefer frequent and longer terms; drop a shorter term contained in a chosen longer one with as many rows.
        var chosen = new List<string>();
        foreach (var (term, count) in counted.OrderByDescending(c => c.Rows).ThenByDescending(c => c.Term.Length))
        {
            if (chosen.Count >= max)
            {
                break;
            }

            if (chosen.Any(c => ContainsSessionTerm(c, term) && counted.First(x => x.Term == c).Rows >= count))
            {
                continue;
            }

            chosen.Add(term);
        }

        return chosen;
    }

    private async Task SeedSubtermsAsync(
        TranslateIdsRequest request,
        PromptCache? promptCache,
        PlaceholderMasker placeholderMasker,
        System.Text.Json.JsonElement responseSchema,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items)
    {
        var memory = Ctx.SessionTermMemory;
        if (!Ctx.EnableSessionTermMemory || memory == null)
        {
            return;
        }

        var terms = SelectSubtermSeeds(
            items.Select(it => (it.Source, GetRecForId(it.Id) ?? "", it.Masked)).ToList(),
            memory.Knows,
            MaxSubtermSeeds);
        if (terms.Count == 0)
        {
            return;
        }

        var translationItems = terms.Select((term, i) => new TranslationItem(-(i + 1L), term, Style: SubtermStyleHint)).ToList();
        var batch = terms.Select((term, i) =>
        {
            var mask = placeholderMasker.Mask(term);
            return (Id: -(i + 1L), Source: term, Masked: mask.Text, Mask: mask,
                Glossary: new GlossaryApplication(mask.Text, new Dictionary<string, string>(), Array.Empty<(string, string)>()));
        }).ToList();

        var ctx = new PipelineContext(
            ApiKey: request.ApiKey,
            ModelName: request.ModelName,
            SystemPrompt: request.SystemPrompt,
            EnableApiKeyFailover: request.EnableApiKeyFailover,
            PromptCache: promptCache,
            SourceLang: request.SourceLang,
            TargetLang: request.TargetLang,
            MaxChars: request.MaxChars,
            Temperature: request.Temperature,
            MaxOutputTokens: request.MaxOutputTokens,
            ResponseSchema: responseSchema,
            MaxRetries: request.MaxRetries,
            EnableRepairPass: request.EnableRepairPass,
            PlaceholderMasker: placeholderMasker,
            OnRowUpdated: request.OnRowUpdated,
            CancellationToken: request.CancellationToken
        );

        IReadOnlyDictionary<long, string> results;
        try
        {
            var prompt = TranslationPrompt.BuildUserPrompt(request.SourceLang, request.TargetLang, translationItems,
                Array.Empty<(string Source, string Target)>());
            results = await TranslateBatchOnceAsync(CreateBatchTranslateContext(ctx), batch, prompt,
                await GetPromptCacheNameAsync(promptCache, request.CancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   && !IsRunGenerationLimit(ex) && !IsRateLimitAbort(ex) && !IsCredentialError(ex))
        {
            // Seeding is an aid: the rows themselves still translate without it.
            return;
        }

        foreach (var (id, translated) in results)
        {
            var index = (int)(-id - 1);
            var target = (translated ?? "").Trim();
            if (index >= 0 && index < terms.Count && IsSessionTermTranslationText(target))
            {
                memory.TryLearn(terms[index], target, allowForce: false);
            }
        }
    }
}
