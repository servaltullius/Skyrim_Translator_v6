using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task TranslateSingleRowAsync(
        PipelineContext ctx,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row
    )
    {
        ctx.CancellationToken.ThrowIfCancellationRequested();
        using var generationScope = EnterGenerationScope(new[] { row.Id });
        row = PrepareRowWithSessionTermForceTokens(row);

        try
        {
            await TranslateSingleRowCoreAsync(ctx, row);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRunGenerationLimit(ex) || IsRateLimitAbort(ex) || IsCredentialError(ex) || (ctx.EnableApiKeyFailover && IsApiKeyFailoverError(ex, ctx.CancellationToken)))
        {
            throw;
        }
        catch (Exception ex)
        {
            // A one-row batch ends here, not in ProcessBatchAsync, so it counts toward the rate-limit streak here.
            ThrowIfRateLimitStreakReached(ex, ctx.EnableApiKeyFailover);
            await HandleRowErrorAsync(row.Id, ex, ctx.OnRowUpdated, awaitNotifications: true, ctx.CancellationToken);
        }
    }

    private async Task TranslateSingleRowCoreAsync(
        PipelineContext ctx,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row
    )
    {
        var rec = GetRecForId(row.Id);
        var styleHint = GuessStyleHint(row.Source, Ctx.UseRecStyleHints ? rec : null);
        styleHint = AppendDialogueContextToStyleHint(styleHint, GetDialogueContextWindowForId(row.Id));
        styleHint = AppendReferences(row.Id, styleHint);

        var raw = await TranslateRowRawAsync(ctx, row, styleHint);
        raw = await TrySemanticRepairAsync(ctx, row, styleHint, raw);

        var final = ApplyTokensAndUnmask(raw, row.Glossary, row.Mask, ctx.PlaceholderMasker, ctx.TargetLang);
        if (Ctx.EnableTemplateFixer)
        {
            final = MagDurPlaceholderFixer.Fix(row.Source, final, ctx.TargetLang);
        }
        final = PlaceholderUnitBinder.EnforceUnitsFromSource(ctx.TargetLang, row.Source, final);
        final = KoreanProtectFromFixer.Fix(ctx.TargetLang, row.Source, final);
        final = KoreanSourceAwareFixes.Apply(ctx.TargetLang, row.Source, final);
        final = KoreanTranslationFixer.Fix(ctx.TargetLang, final);
        TokenValidator.ValidateFinalTextIntegrity(row.Source, final, context: $"id={row.Id} post-edits");

        (raw, final) = await MaybeApplyQualityEscalationAsync(ctx, row, styleHint, raw, final);
        TryLearnSessionTermMemory(row.Id, row.Source, final);

        await _db.UpdateStringTranslationAsync(row.Id, final, StringEntryStatus.Done, null, ctx.CancellationToken);
        if (ctx.OnRowUpdated != null)
        {
            await ctx.OnRowUpdated(row.Id, StringEntryStatus.Done, final);
        }

        var duplicateDoneUpdates = await BuildDuplicateDoneUpdatesAsync(
            canonicalId: row.Id,
            rawText: raw,
            glossary: row.Glossary,
            placeholderMasker: ctx.PlaceholderMasker,
            targetLang: ctx.TargetLang,
            onRowUpdated: ctx.OnRowUpdated,
            awaitNotifications: true,
            cancellationToken: ctx.CancellationToken
        );

        if (duplicateDoneUpdates.Count == 0)
        {
            return;
        }

        await _db.UpdateStringTranslationsAsync(duplicateDoneUpdates, ctx.CancellationToken);
        if (ctx.OnRowUpdated != null)
        {
            foreach (var it in duplicateDoneUpdates)
            {
                await ctx.OnRowUpdated(it.Id, StringEntryStatus.Done, it.DestText);
            }
        }
    }

    private async Task<string> TranslateRowRawAsync(
        PipelineContext ctx,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row,
        string? styleHint
    )
    {
        if (row.Masked.Length > ctx.MaxChars)
        {
            return await TranslateLongMaskedTextAsync(ctx, row);
        }

        try
        {
            var translateRequest = new TextRequestContext(
                ApiKey: ctx.ApiKey,
                ModelName: ctx.ModelName,
                SystemPrompt: ctx.SystemPrompt,
                PromptCache: ctx.PromptCache,
                Lane: RequestLane.General,
                SourceLang: ctx.SourceLang,
                TargetLang: ctx.TargetLang,
                Temperature: ctx.Temperature,
                MaxOutputTokens: ctx.MaxOutputTokens,
                MaxRetries: ctx.MaxRetries,
                CancellationToken: ctx.CancellationToken,
                CandidateCount: GeminiModelPolicy.SupportsMultipleCandidates(ctx.ModelName)
                    ? GetRiskyCandidateCountForSource(ctx.TargetLang, row.Source)
                    : 1,
                Edid: GetEdidForId(row.Id)
            );

            return await TranslateTextWithSentinelAsync(
                translateRequest,
                row.Masked,
                row.Glossary.PromptOnlyPairs,
                new TextWithSentinelContext(
                    GlossaryTokenToReplacement: row.Glossary.TokenToReplacement,
                    StyleHint: styleHint,
                    Context: $"id={row.Id}",
                    SourceTextForTranslationMemory: row.Source
                )
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!MustStopRecovery(ex))
        {
            using var recovery = EnterGenerationScope(recovery: true);
            return await TranslateLongMaskedTextAsync(ctx, row);
        }
    }

    private async Task<string> TrySemanticRepairAsync(
        PipelineContext ctx,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row,
        string? styleHint,
        string raw
    )
    {
        if (!ctx.EnableRepairPass || !TokenSanitizer.NeedsPlaceholderSemanticRepair(row.Masked, raw, ctx.TargetLang, Ctx.SemanticRepairMode))
        {
            return raw;
        }

        try
        {
            using var recovery = EnterGenerationScope(new[] { row.Id }, recovery: true);
            var repairPrompt = BuildSemanticRepairPrompt(
                ctx,
                maskedSource: row.Masked,
                currentTranslation: raw,
                promptOnlyGlossary: row.Glossary.PromptOnlyPairs,
                styleHint: styleHint,
                edid: GetEdidForId(row.Id)
            );
            var repairRequest = CreateSemanticRepairRequest(ctx) with { MaxOutputTokens = TranslationOutputBudget.Compute(
                row.Masked.Length, TranslationConstants.XtTokenRegex.Matches(row.Masked).Count, 1,
                ctx.MaxOutputTokens, Ctx.EnableAdaptiveOutputBudget), SourceText = row.Masked };
            var repairedText = await TranslateUserPromptWithRetriesAsync(repairRequest, repairPrompt);

            return TokenSanitizer.EnsureTokensPreservedOrRepair(
                row.Masked,
                repairedText,
                context: $"id={row.Id} semantic-repair",
                glossaryTokenToReplacement: row.Glossary.TokenToReplacement
            );
        }
        catch (OperationCanceledException) when (ctx.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!IsRunGenerationLimit(ex) && !IsCredentialError(ex))
        {
            return raw;
        }
    }

    private string BuildSemanticRepairPrompt(
        PipelineContext ctx,
        string maskedSource,
        string currentTranslation,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary,
        string? styleHint,
        string? edid
    )
    {
        return TranslationPrompt.BuildRepairTextOnlyUserPrompt(
            new TranslationPrompt.RepairTextOnlyPromptRequest(
                SourceLang: ctx.SourceLang,
                TargetLang: ctx.TargetLang,
                SourceText: maskedSource,
                CurrentTranslation: currentTranslation,
                PromptOnlyGlossary: MergeSessionPromptOnlyGlossaryForText(maskedSource, promptOnlyGlossary),
                StyleHint: styleHint,
                Edid: edid
            )
        );
    }

    private static TextRequestContext CreateSemanticRepairRequest(PipelineContext ctx)
        => new(
            ApiKey: ctx.ApiKey,
            ModelName: ctx.ModelName,
            SystemPrompt: ctx.SystemPrompt,
            PromptCache: ctx.PromptCache,
            Lane: RequestLane.General,
            SourceLang: ctx.SourceLang,
            TargetLang: ctx.TargetLang,
            Temperature: 0.0,
            MaxOutputTokens: ctx.MaxOutputTokens,
            MaxRetries: 1,
            CancellationToken: ctx.CancellationToken,
            Purpose: "repair-semantic"
        );

	    private async Task<string> TranslateLongMaskedTextAsync(
	        PipelineContext ctx,
	        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) row
	    )
	    {
	        var rec = GetRecForId(row.Id);
	        var styleHint = GuessStyleHint(row.Source, Ctx.UseRecStyleHints ? rec : null);
	        styleHint = AppendDialogueContextToStyleHint(styleHint, GetDialogueContextWindowForId(row.Id));
	        // Long texts are split into chunks; a 600-character excerpt of the earlier translation would only fit the first one.
	        styleHint = AppendBookTitleReference(row.Id, styleHint);
	        var tokenCount = TranslationConstants.XtTokenRegex.Matches(row.Masked).Count;
	        var initialChunkChars = ComputeLongTextInitialChunkChars(ctx, tokenCount);

	        var minChunkChars = 256;
	        var longTemperature = Math.Min(ctx.Temperature, 0.05);

	        var chunkContext = new LongTextChunkContext(
	            ApiKey: ctx.ApiKey,
	            ModelName: ctx.ModelName,
	            SystemPrompt: ctx.SystemPrompt,
	            PromptCache: ctx.PromptCache,
	            SourceLang: ctx.SourceLang,
	            TargetLang: ctx.TargetLang,
	            Row: row,
	            Temperature: longTemperature,
	            MaxOutputTokens: ctx.MaxOutputTokens,
	            MaxRetries: ctx.MaxRetries,
	            StyleHint: styleHint,
	            CancellationToken: ctx.CancellationToken
	        );

	        using var firstPass = EnterChunkFirstPassScope();
	        return await TranslateChunkWithAdaptiveSplittingAsync(
	            chunkContext,
	            row.Masked,
	            initialChunkChars,
	            minChunkChars
	        );
	    }

	    private static string? AppendDialogueContextToStyleHint(string? styleHint, string? dialogueContextWindow)
	    {
	        if (string.IsNullOrWhiteSpace(dialogueContextWindow))
	        {
	            return styleHint;
	        }

	        var ctx = dialogueContextWindow.Trim();
	        if (ctx.Length == 0)
	        {
	            return styleHint;
	        }

	        if (string.IsNullOrWhiteSpace(styleHint))
	        {
	            return $"Context (reference only):\n{ctx}";
	        }

	        return $"{styleHint.Trim()}\n\nContext (reference only):\n{ctx}";
	    }

	    private int ComputeLongTextInitialChunkChars(PipelineContext ctx, int tokenCount)
	    {
	        var initialChunkChars = Math.Min(ctx.MaxChars, 6000);
	        if (IsCjkLanguage(ctx.TargetLang))
	        {
	            // CJK outputs can hit output token limits sooner; keep chunks smaller to avoid MAX_TOKENS truncation.
	            initialChunkChars = Math.Min(initialChunkChars, 4500);
	        }
	        if (Ctx.MaskedTokensPerCharHint is > 0)
	        {
	            var targetTokens = GetLongTextTargetOutputTokens(ctx.MaxOutputTokens);
	            var estimated = (int)Math.Floor(targetTokens / Ctx.MaskedTokensPerCharHint.Value);
	            if (estimated >= 256)
	            {
	                initialChunkChars = Math.Min(initialChunkChars, estimated);
	            }
	        }

	        if (tokenCount >= 80)
	        {
	            initialChunkChars = Math.Min(initialChunkChars, 3000);
	        }
	        else if (tokenCount >= 40)
	        {
	            initialChunkChars = Math.Min(initialChunkChars, 4500);
	        }

	        return initialChunkChars;
	    }
	}
