using System;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private bool TryGetQualityEscalationTrigger(
        string sourceText,
        string destText,
        string targetLang,
        out string code,
        out string message
    )
    {
        code = "";
        message = "";

        if (!LanguageHelper.IsKoreanLanguage(targetLang))
        {
            return false;
        }

        if (LqaHeuristics.IsLikelyUntranslated(sourceText, destText))
        {
            code = "untranslated";
            message = "번역문이 원문과 동일합니다.";
            return true;
        }

        if (LqaHeuristics.HasUnresolvedParticleMarkers(destText))
        {
            code = "particle_marker";
            message = "조사 표기(괄호/슬래시 형태)가 그대로 남아있습니다.";
            return true;
        }

        var dup = LqaHeuristics.FindDuplicationArtifactExample(destText);
        if (!string.IsNullOrWhiteSpace(dup))
        {
            code = "dup_artifact";
            message = $"중복/오타 패턴이 감지되었습니다: '{dup}'.";
            return true;
        }

        var pct = LqaHeuristics.FindPercentArtifactExample(destText);
        if (!string.IsNullOrWhiteSpace(pct))
        {
            code = "percent_artifact";
            message = $"퍼센트 표기 오류 가능성: '{pct}'.";
            return true;
        }

        return false;
    }

    private async Task<(string Raw, string Final)> MaybeApplyQualityEscalationAsync(
        PipelineContext ctx,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row,
        string? styleHint,
        string raw,
        string final
    )
    {
        if (!Ctx.EnableQualityEscalation || string.IsNullOrWhiteSpace(Ctx.QualityEscalationModelName))
        {
            return (raw, final);
        }

        var escalationModel = Ctx.QualityEscalationModelName!.Trim();
        if (string.IsNullOrWhiteSpace(escalationModel)
            || string.Equals(escalationModel, ctx.ModelName, StringComparison.OrdinalIgnoreCase))
        {
            return (raw, final);
        }

        if (!TryGetQualityEscalationTrigger(row.Source, final, ctx.TargetLang, out _, out _))
        {
            return (raw, final);
        }

        try
        {
            using var recovery = EnterGenerationScope(new[] { row.Id }, recovery: true);
            // Avoid prompt cache calls during escalation to keep request count low and because cache is per-model.
            var escCtx = ctx with { ModelName = escalationModel, PromptCache = null };

            var escRaw = await TranslateRowRawAsync(escCtx, row, styleHint);
            escRaw = await TrySemanticRepairAsync(escCtx, row, styleHint, escRaw);

            var escFinal = ApplyTokensAndUnmask(escRaw, row.Glossary, row.Mask, escCtx.PlaceholderMasker, escCtx.TargetLang);
            if (Ctx.EnableTemplateFixer)
            {
                escFinal = MagDurPlaceholderFixer.Fix(row.Source, escFinal, escCtx.TargetLang);
            }

            escFinal = PlaceholderUnitBinder.EnforceUnitsFromSource(escCtx.TargetLang, row.Source, escFinal);
            escFinal = KoreanProtectFromFixer.Fix(escCtx.TargetLang, row.Source, escFinal);
            escFinal = KoreanSourceAwareFixes.Apply(escCtx.TargetLang, row.Source, escFinal);
            escFinal = KoreanTranslationFixer.Fix(escCtx.TargetLang, escFinal);
            TokenValidator.ValidateFinalTextIntegrity(row.Source, escFinal, context: $"id={row.Id} quality-escalation post-edits");

            if (TryGetQualityEscalationTrigger(row.Source, escFinal, escCtx.TargetLang, out _, out _))
            {
                return (raw, final);
            }

            return (escRaw, escFinal);
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Escalation is optional: keep the validated baseline if its request fails
            // or cannot fit the shared generation budget. Required work still observes the cap.
            return (raw, final);
        }
    }

    // Note: TranslationService already has IsKoreanLanguage helpers used by semantic repair.
}
