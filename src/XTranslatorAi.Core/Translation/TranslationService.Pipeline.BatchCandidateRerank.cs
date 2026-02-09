using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private int GetBatchCandidateCount(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        if (batch.Count == 0)
        {
            return 1;
        }

        for (var i = 0; i < batch.Count; i++)
        {
            var candidateCount = GetRiskyCandidateCountForSource(ctx.TargetLang, batch[i].Source);
            if (candidateCount > 1)
            {
                return candidateCount;
            }
        }

        return 1;
    }

    private int GetRiskyCandidateCountForSource(string targetLang, string sourceText)
    {
        if (!_enableRiskyCandidateRerank
            || !IsKoreanLanguage(targetLang)
            || !IsStructuralRiskSourceText(sourceText))
        {
            return 1;
        }

        return Math.Clamp(_riskyCandidateCount, 2, 8);
    }

    private static bool IsStructuralRiskSourceText(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var lower = sourceText.ToLowerInvariant();
        if (lower.IndexOf("protect", StringComparison.Ordinal) >= 0
            && lower.IndexOf(" from ", StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        if (lower.IndexOf(" against ", StringComparison.Ordinal) >= 0
            || lower.IndexOf(" between ", StringComparison.Ordinal) >= 0 && lower.IndexOf(" and ", StringComparison.Ordinal) >= 0
            || lower.IndexOf(" instead of ", StringComparison.Ordinal) >= 0
            || lower.IndexOf(" rather than ", StringComparison.Ordinal) >= 0
            || lower.IndexOf(" unless ", StringComparison.Ordinal) >= 0
            || lower.IndexOf(" except ", StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        return lower.IndexOf('/', StringComparison.Ordinal) >= 0
               && (lower.IndexOf(" per ", StringComparison.Ordinal) >= 0
                   || lower.IndexOf(" each ", StringComparison.Ordinal) >= 0);
    }

    private string SelectBestBatchCandidateText(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        IReadOnlyList<string> candidateTexts
    )
    {
        if (candidateTexts.Count == 0)
        {
            throw new InvalidOperationException("GenerateContent returned no candidate texts.");
        }

        var bestText = candidateTexts[0];
        var bestScore = int.MinValue;
        var hasBest = false;

        for (var i = 0; i < candidateTexts.Count; i++)
        {
            var text = candidateTexts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            IReadOnlyDictionary<long, string> map;
            try
            {
                map = ParseAndValidateBatchMap(text, batch.Count);
            }
            catch
            {
                continue;
            }

            var score = ScoreBatchCandidateMap(ctx, batch, map);
            if (!hasBest || score > bestScore)
            {
                hasBest = true;
                bestScore = score;
                bestText = text;
            }
        }

        return bestText;
    }

    private int ScoreBatchCandidateMap(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        IReadOnlyDictionary<long, string> map
    )
    {
        var score = 0;

        for (var i = 0; i < batch.Count; i++)
        {
            var it = batch[i];
            if (!map.TryGetValue(it.Id, out var output))
            {
                score -= 400;
                continue;
            }

            output = PlaceholderSemanticHintInjector.Strip(output);
            output = GlossarySemanticHintInjector.Strip(output);

            string candidate;
            try
            {
                candidate = EnsureTokensPreservedOrRepair(
                    it.Masked,
                    output,
                    context: $"id={it.Id} candidate-rerank",
                    glossaryTokenToReplacement: it.Glossary.TokenToReplacement
                );
                score += 30;
            }
            catch (InvalidOperationException)
            {
                score -= 300;
                continue;
            }

            if (NeedsPlaceholderSemanticRepair(it.Masked, candidate, ctx.TargetLang, _semanticRepairMode))
            {
                score -= 60;
            }

            if (TryGetQualityEscalationTrigger(it.Source, candidate, ctx.TargetLang, out _, out _))
            {
                score -= 40;
            }

            if (LqaHeuristics.IsLikelyUntranslated(it.Source, candidate))
            {
                score -= 80;
            }

            if (LqaHeuristics.HasUnresolvedParticleMarkers(candidate))
            {
                score -= 30;
            }
        }

        return score;
    }
}
