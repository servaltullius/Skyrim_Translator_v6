using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private enum RequestLane
    {
        General,
        VeryLong,
    }

    internal sealed record RowContext(string? Rec, string? Edid);

    private string? GetRecForId(long id)
    {
        if (Ctx.RowContextById == null)
        {
            return null;
        }

        return Ctx.RowContextById.TryGetValue(id, out var ctx) ? ctx.Rec : null;
    }

    private string? GetEdidForId(long id)
    {
        if (Ctx.RowContextById == null)
        {
            return null;
        }

        return Ctx.RowContextById.TryGetValue(id, out var ctx) ? ctx.Edid : null;
    }

    private string? GetDialogueContextWindowForId(long id)
    {
        if (Ctx.DialogueContextWindowById == null)
        {
            return null;
        }

        return Ctx.DialogueContextWindowById.TryGetValue(id, out var ctx) ? ctx : null;
    }

    /// <summary>Adds the reference material for a row: its book title and its earlier translation.</summary>
    private string? AppendReferences(long id, string? hint)
    {
        hint = AppendBookTitleReference(id, hint);
        var previous = Ctx.PreviousTranslationById?.TryGetValue(id, out var text) == true
            ? TranslationPreviousReference.Build(text)
            : null;
        return previous == null ? hint : string.IsNullOrWhiteSpace(hint) ? previous : hint + "\n\n" + previous;
    }

    private string? AppendBookTitleReference(long id, string? hint)
    {
        if (!Ctx.EnableBookContext || !TranslationBookContext.IsBody(GetRecForId(id))) return hint;
        var edid = GetEdidForId(id)?.Trim();
        var title = edid != null && Ctx.BookTitlesByEdid?.TryGetValue(edid, out var found) == true ? found : null;
        var reference = TranslationBookContext.Build(title: title);
        return reference == null ? hint : string.IsNullOrWhiteSpace(hint) ? reference : hint + "\n\n" + reference;
    }

    private async Task<string> GenerateContentWithGateAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        RequestLane lane,
        CancellationToken cancellationToken
    )
    {
        await WaitForGlobalThrottleAsync(cancellationToken);

        var laneGate = lane == RequestLane.VeryLong ? Ctx.VeryLongRequestGate : null;
        var gate = Ctx.GenerateContentGate;
        if (gate == null && laneGate == null)
        {
            ConsumeGenerationBudget();
            return await _gemini.GenerateContentAsync(apiKey, modelName, request, cancellationToken);
        }

        if (laneGate != null)
        {
            await laneGate.WaitAsync(cancellationToken);
        }

        var adaptiveAcquired = false;
        try
        {
            if (gate != null)
            {
                await gate.WaitAsync(cancellationToken);
            }

            try
            {
                await Ctx.AdaptiveConcurrency.WaitForSlotAsync(cancellationToken);
                adaptiveAcquired = true;
                ConsumeGenerationBudget();
                var response = await _gemini.GenerateContentAsync(apiKey, modelName, request, cancellationToken);
                Ctx.AdaptiveConcurrency.RegisterSuccess();
                return response;
            }
            finally
            {
                if (adaptiveAcquired)
                {
                    Ctx.AdaptiveConcurrency.ReleaseSlot();
                }
                gate?.Release();
            }
        }
        finally
        {
            laneGate?.Release();
        }
    }

    private async Task<IReadOnlyList<string>> GenerateContentCandidatesWithGateAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        RequestLane lane,
        CancellationToken cancellationToken
    )
    {
        await WaitForGlobalThrottleAsync(cancellationToken);

        var laneGate = lane == RequestLane.VeryLong ? Ctx.VeryLongRequestGate : null;
        var gate = Ctx.GenerateContentGate;
        if (gate == null && laneGate == null)
        {
            ConsumeGenerationBudget();
            return await _gemini.GenerateContentCandidatesAsync(apiKey, modelName, request, cancellationToken);
        }

        if (laneGate != null)
        {
            await laneGate.WaitAsync(cancellationToken);
        }

        var adaptiveAcquired = false;
        try
        {
            if (gate != null)
            {
                await gate.WaitAsync(cancellationToken);
            }

            try
            {
                await Ctx.AdaptiveConcurrency.WaitForSlotAsync(cancellationToken);
                adaptiveAcquired = true;
                ConsumeGenerationBudget();
                var response = await _gemini.GenerateContentCandidatesAsync(apiKey, modelName, request, cancellationToken);
                Ctx.AdaptiveConcurrency.RegisterSuccess();
                return response;
            }
            finally
            {
                if (adaptiveAcquired)
                {
                    Ctx.AdaptiveConcurrency.ReleaseSlot();
                }
                gate?.Release();
            }
        }
        finally
        {
            laneGate?.Release();
        }
    }

    private async Task<int> CountTokensWithGateAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
    {
        await WaitForGlobalThrottleAsync(cancellationToken);

        var gate = Ctx.GenerateContentGate;
        if (gate == null)
        {
            return await _gemini.CountTokensAsync(apiKey, modelName, text, cancellationToken);
        }

        await gate.WaitAsync(cancellationToken);
        var adaptiveAcquired = false;
        try
        {
            await Ctx.AdaptiveConcurrency.WaitForSlotAsync(cancellationToken);
            adaptiveAcquired = true;
            var tokenCount = await _gemini.CountTokensAsync(apiKey, modelName, text, cancellationToken);
            Ctx.AdaptiveConcurrency.RegisterSuccess();
            return tokenCount;
        }
        finally
        {
            if (adaptiveAcquired)
            {
                Ctx.AdaptiveConcurrency.ReleaseSlot();
            }
            gate.Release();
        }
    }

    private enum BatchPreference
    {
        ShortFirst,
        LongFirst,
        VeryLongFirst,
    }

    private enum BatchSource
    {
        None,
        Short,
        Long,
        VeryLong,
    }

    private sealed record PipelineContext(
        string ApiKey,
        string ModelName,
        string SystemPrompt,
        bool EnableApiKeyFailover,
        PromptCache? PromptCache,
        string SourceLang,
        string TargetLang,
        int MaxChars,
        double Temperature,
        int MaxOutputTokens,
        System.Text.Json.JsonElement ResponseSchema,
        int MaxRetries,
        bool EnableRepairPass,
        PlaceholderMasker PlaceholderMasker,
        Func<long, StringEntryStatus, string, Task>? OnRowUpdated,
        CancellationToken CancellationToken
    );

    private sealed record PendingRepair(
        long Id,
        string Source,
        string Masked,
        GlossaryApplication Glossary,
        string Current
    );
}
