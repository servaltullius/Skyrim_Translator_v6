using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Boilerplate sentences repeated across a mod's descriptions came out differently row by row: Elden Rim's
/// "A skill beyond the reach of most." four ways per War Ash pack, "When it added as Favorites, you can activate the
/// effect with Favorites hotkeys." with 발동 and 활성화. Identical rows are translated once, but a sentence shared by
/// different rows was not. Before the run, sentences that recur in at least three description rows are translated
/// once and offered to every later request whose text contains them, as reference pairs (not forced).
/// </summary>
public sealed partial class TranslationService
{
    internal const int MaxRepeatedSentenceSeeds = 20;
    private const int MinSentenceRows = 3;

    private static readonly Regex SentenceSplitRegex = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant);

    private const string SentenceStyleHint =
        "A sentence that recurs in many descriptions of this mod. Translate it once, in 합니다체, exactly as it should "
        + "read every time it appears.";

    internal static IReadOnlyList<string> SelectRepeatedSentences(IReadOnlyList<(string Masked, string Rec)> rows, int max)
    {
        var rowsBySentence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (masked, rec) in rows)
        {
            var family = LqaScanner.GetRecBase(rec);
            if (family is "INFO" or "DIAL" or "BOOK" || string.IsNullOrWhiteSpace(masked))
            {
                continue;
            }

            foreach (var sentence in SentenceSplitRegex.Split(masked.Trim()).Select(s => s.Trim()).Distinct(StringComparer.Ordinal))
            {
                if (IsRepeatableSentence(sentence))
                {
                    rowsBySentence[sentence] = rowsBySentence.GetValueOrDefault(sentence) + 1;
                }
            }
        }

        return rowsBySentence
            .Where(p => p.Value >= MinSentenceRows)
            .OrderByDescending(p => p.Value * p.Key.Length)
            .Take(max)
            .Select(p => p.Key)
            .ToList();
    }

    private static bool IsRepeatableSentence(string sentence)
        => sentence.Length is >= 15 and <= 220
           && sentence[^1] is '.' or '!' or '?'
           && sentence.Count(char.IsWhiteSpace) >= 2
           && sentence.Any(char.IsAsciiLetterLower)
           && !sentence.Contains("__XT_", StringComparison.Ordinal)
           && sentence.IndexOfAny(new[] { '\r', '\n', '<', '>', '[', ']' }) < 0;

    private async Task SeedRepeatedSentencesAsync(
        TranslateIdsRequest request,
        PromptCache? promptCache,
        PlaceholderMasker placeholderMasker,
        System.Text.Json.JsonElement responseSchema,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items)
    {
        var sentences = SelectRepeatedSentences(items.Select(it => (it.Masked, GetRecForId(it.Id) ?? "")).ToList(), MaxRepeatedSentenceSeeds);
        if (sentences.Count == 0)
        {
            return;
        }

        var translationItems = sentences.Select((s, i) => new TranslationItem(-(i + 1L), s, Style: SentenceStyleHint)).ToList();
        var batch = sentences.Select((s, i) =>
        {
            var mask = new MaskedText(s, new Dictionary<string, string>());
            return (Id: -(i + 1L), Source: s, Masked: s, Mask: mask,
                Glossary: new GlossaryApplication(s, new Dictionary<string, string>(), Array.Empty<(string, string)>()));
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
                MergeSessionPromptOnlyGlossaryForTexts(sentences, Array.Empty<(string Source, string Target)>()));
            results = await TranslateBatchOnceAsync(CreateBatchTranslateContext(ctx), batch, prompt,
                await GetPromptCacheNameAsync(promptCache, request.CancellationToken));
        }
        // A timeout (HttpClient.Timeout) is a TaskCanceledException too; only the user's stop ends the run here.
        catch (Exception ex) when ((ex is not OperationCanceledException || !request.CancellationToken.IsCancellationRequested)
                                   && !IsRunGenerationLimit(ex) && !IsRateLimitAbort(ex) && !IsCredentialError(ex))
        {
            return; // An aid only: the rows still translate without it.
        }

        foreach (var (id, translated) in results)
        {
            var index = (int)(-id - 1);
            var target = (translated ?? "").Trim();
            if (index >= 0 && index < sentences.Count && target.Length > 0 && !target.Contains("__XT_", StringComparison.Ordinal))
            {
                Ctx.RepeatedSentences[sentences[index]] = target;
            }
        }
    }

    /// <summary>The seeded sentence translations whose source sentence appears in one of <paramref name="texts"/>.</summary>
    private IReadOnlyList<(string Source, string Target)> AddRepeatedSentencePairs(
        IReadOnlyList<string> texts, IReadOnlyList<(string Source, string Target)> pairs)
    {
        if (Ctx.RepeatedSentences.IsEmpty)
        {
            return pairs;
        }

        var added = Ctx.RepeatedSentences
            .Where(p => texts.Any(t => t != null && t.Contains(p.Key, StringComparison.Ordinal)))
            .Where(p => !pairs.Any(existing => string.Equals(existing.Source, p.Key, StringComparison.Ordinal)))
            .Select(p => (p.Key, p.Value))
            .ToList();
        return added.Count == 0 ? pairs : pairs.Concat(added).ToList();
    }
}
