using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private sealed record VeryLongPromptConfig(
        bool IsCjk,
        bool IsGemini3,
        double? MaskedTokensPerCharHint,
        int MaxChars,
        int MaxOutputTokens
    );

    private static void BuildPromptsForBatches(
        string sourceLang,
        string targetLang,
        IReadOnlyList<IReadOnlyList<PreparedRow>> batches,
        List<string> batchPrompts,
        List<string> textPrompts
    )
    {
        foreach (var batch in batches)
        {
            if (batch.Count <= 0)
            {
                continue;
            }

            if (batch.Count == 1)
            {
                var it = batch[0];
                var styleHint = GuessStyleHint(it.Source);
                var withSentinel = it.Masked + " " + EndSentinelToken;
                textPrompts.Add(TranslationPrompt.BuildTextOnlyUserPrompt(sourceLang, targetLang, withSentinel, it.PromptOnlyPairs, styleHint));
                continue;
            }

            var requestItems = new List<TranslationItem>(batch.Count);
            var promptOnlyPairs = CollectPromptOnlyPairs(batch);

            foreach (var it in batch)
            {
                requestItems.Add(new TranslationItem(it.Id, it.Masked));
            }

            batchPrompts.Add(TranslationPrompt.BuildUserPrompt(sourceLang, targetLang, requestItems, promptOnlyPairs));
        }
    }

    private static void BuildPromptsForVeryLong(
        string sourceLang,
        string targetLang,
        IReadOnlyList<PreparedRow> veryLongItems,
        VeryLongPromptConfig cfg,
        List<string> textPrompts
    )
    {
        if (veryLongItems.Count == 0)
        {
            return;
        }

        var baseChunkChars = Math.Min(cfg.MaxChars, cfg.IsCjk ? 4500 : 6000);
        var maxTokensPerChunk = GetMaxTokensPerChunkForVeryLong(cfg.IsGemini3, cfg.IsCjk);

        foreach (var it in veryLongItems)
        {
            var tokenCount = XtTokenRegex.Matches(it.Masked).Count;
            var chunkChars = ComputeVeryLongChunkChars(
                baseChunkChars,
                tokenCount,
                cfg.MaskedTokensPerCharHint,
                cfg.MaxOutputTokens
            );

            var parts = TokenAwareTextSplitter.Split(it.Masked, chunkChars, maxTokensPerChunk);
            var styleHint = GuessStyleHint(it.Source);
            foreach (var part in parts)
            {
                var withSentinel = part + " " + EndSentinelToken;
                textPrompts.Add(TranslationPrompt.BuildTextOnlyUserPrompt(sourceLang, targetLang, withSentinel, it.PromptOnlyPairs, styleHint));
            }
        }
    }

    private static int GetMaxTokensPerChunkForVeryLong(bool isGemini3, bool isCjk)
    {
        if (isGemini3)
        {
            return isCjk ? 24 : 32;
        }

        return isCjk ? 30 : 40;
    }

    private static int ComputeVeryLongChunkChars(
        int baseChunkChars,
        int tokenCount,
        double? maskedTokensPerCharHint,
        int maxOutputTokens
    )
    {
        var chunkChars = baseChunkChars;

        if (maskedTokensPerCharHint is > 0)
        {
            var targetTokens = GetLongTextTargetOutputTokens(maxOutputTokens);
            var estimated = (int)Math.Floor(targetTokens / maskedTokensPerCharHint.Value);
            if (estimated >= 256)
            {
                chunkChars = Math.Min(chunkChars, estimated);
            }
        }

        if (tokenCount >= 80)
        {
            chunkChars = Math.Min(chunkChars, 3000);
        }
        else if (tokenCount >= 40)
        {
            chunkChars = Math.Min(chunkChars, 4500);
        }

        return chunkChars;
    }

    private async Task<(List<string> BatchPrompts, List<string> TextPrompts)> BuildEstimationPromptsAsync(
        string apiKey,
        string modelName,
        string sourceLang,
        string targetLang,
        PreparedEstimationInput prepared,
        int batchSize,
        int maxChars,
        int maxOutputTokens,
        CancellationToken cancellationToken
    )
    {
        // Mirror TranslationService classification.
        var shortItems = new List<PreparedRow>();
        var longItems = new List<PreparedRow>();
        var veryLongItems = new List<PreparedRow>();

        var shortMax = Math.Clamp(maxChars / 3, min: 1500, max: 6000);
        foreach (var it in prepared.Items)
        {
            if (it.Masked.Length > maxChars)
            {
                veryLongItems.Add(it);
            }
            else if (it.Masked.Length <= shortMax)
            {
                shortItems.Add(it);
            }
            else
            {
                longItems.Add(it);
            }
        }

        string GetGroupKey(PreparedRow row)
        {
            if (!prepared.ContextById.TryGetValue(row.Id, out var ctx))
            {
                ctx = (null, null);
            }

            return TranslationBatchGrouping.ComputeGroupKey(ctx.Rec, ctx.Edid, row.Source);
        }

        static string GetRecSortKey(string? rec) => (rec ?? "").Trim();

        void SortForBatchConsistency(List<PreparedRow> list)
        {
            if (list.Count <= 1)
            {
                return;
            }

            var sorted = list
                .Select(
                    (it, idx) =>
                        new
                        {
                            Item = it,
                            Index = idx,
                            Group = GetGroupKey(it),
                            Rec = prepared.ContextById.TryGetValue(it.Id, out var ctx) ? GetRecSortKey(ctx.Rec) : "",
                        }
                )
                .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Rec, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Item.Masked.Length)
                .ThenBy(x => x.Index)
                .Select(x => x.Item)
                .ToList();

            list.Clear();
            list.AddRange(sorted);
        }

        SortForBatchConsistency(shortItems);
        SortForBatchConsistency(longItems);

        double? maskedTokensPerCharHint = null;
        if (veryLongItems.Count > 0)
        {
            try
            {
                var sampleText = veryLongItems[0].Masked;
                var sampleChars = Math.Min(sampleText.Length, 2000);
                if (sampleChars > 0)
                {
                    var sample = sampleText.Substring(0, sampleChars);
                    var sampleTokens = await _gemini.CountTokensAsync(apiKey, modelName, sample, cancellationToken);
                    if (sampleTokens > 0)
                    {
                        maskedTokensPerCharHint = (double)sampleTokens / sampleChars;
                    }
                }
            }
            catch
            {
                maskedTokensPerCharHint = null;
            }
        }

        var shortBatches = TranslationBatching.ChunkBy(shortItems, it => it.Masked.Length, batchSize, maxChars).ToList();
        var longBatchSize = Math.Max(1, Math.Min(batchSize, 8));
        var longBatches = TranslationBatching.ChunkBy(longItems, it => it.Masked.Length, longBatchSize, maxChars).ToList();

        var batchPrompts = new List<string>(capacity: shortBatches.Count + longBatches.Count);
        var textPrompts = new List<string>(capacity: veryLongItems.Count + 16);

        BuildPromptsForBatches(sourceLang, targetLang, shortBatches, batchPrompts, textPrompts);
        BuildPromptsForBatches(sourceLang, targetLang, longBatches, batchPrompts, textPrompts);
        var veryLongConfig = new VeryLongPromptConfig(
            IsCjk: IsCjkLanguage(targetLang),
            IsGemini3: IsGemini3Model(modelName),
            MaskedTokensPerCharHint: maskedTokensPerCharHint,
            MaxChars: maxChars,
            MaxOutputTokens: maxOutputTokens
        );
        BuildPromptsForVeryLong(sourceLang, targetLang, veryLongItems, veryLongConfig, textPrompts);

        return (batchPrompts, textPrompts);
    }

    private static IReadOnlyList<(string Source, string Target)> CollectPromptOnlyPairs(IReadOnlyList<PreparedRow> rows)
    {
        var list = new List<(string Source, string Target)>();
        var set = new HashSet<(string Source, string Target)>(new SourceTargetComparer());

        foreach (var r in rows)
        {
            foreach (var p in r.PromptOnlyPairs)
            {
                if (set.Add(p))
                {
                    list.Add(p);
                }
            }
        }

        return list;
    }

    private sealed class SourceTargetComparer : IEqualityComparer<(string Source, string Target)>
    {
        public bool Equals((string Source, string Target) x, (string Source, string Target) y)
            => string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string Target) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target ?? "")
            );
    }

    private static bool IsCjkLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim().ToLowerInvariant();
        return s is "korean" or "japanese" or "chinese" or "zh" or "ja" or "ko";
    }

    private static bool IsGemini3Model(string modelName)
    {
        var m = GeminiModelPolicy.NormalizeModelName(modelName);
        return m.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetLongTextTargetOutputTokens(int maxOutputTokens)
    {
        if (maxOutputTokens <= 0)
        {
            return 2048;
        }

        var target = (int)Math.Floor(maxOutputTokens * 0.35);
        target = Math.Clamp(target, 512, 6000);

        var headroom = Math.Min(512, Math.Max(128, maxOutputTokens / 10));
        target = Math.Min(target, Math.Max(256, maxOutputTokens - headroom));

        return Math.Max(256, target);
    }

    private static string? GuessStyleHint(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        string? styleHint = null;

        if (sourceText.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) >= 0
            || sourceText.IndexOf("img://Textures/Interface/Books", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            styleHint =
                "This is an in-game book/lore/guide text. Use a neutral written narrative tone in Korean and keep sentence endings consistent. Avoid chatty fillers like \"말이지/야/해\" outside of quoted dialogue. Avoid adding explanatory parentheses like \"(English term)\" unless they exist in the source; prefer natural in-universe rendering for proper nouns.";
        }

        if (styleHint == null)
        {
            return null;
        }

        if (ContainsMultilineItalicBlock(sourceText))
        {
            styleHint +=
                "\n\nFor any <i>...</i> block that is a poem/riddle/inscription, use a solemn archaic literary register (예언/주문/비문 느낌). Prefer endings like \"…리라\", \"…지어다\", \"…것이요/…보여주리라\" and keep line breaks inside the <i> block as-is.";
        }

        return styleHint;
    }

    private static bool ContainsMultilineItalicBlock(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var start = text.IndexOf("<i>", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        var end = text.IndexOf("</i>", start + 3, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return false;
        }

        var inner = text.Substring(start + 3, end - (start + 3));
        return inner.IndexOf('\n') >= 0 || inner.IndexOf('\r') >= 0;
    }
}
