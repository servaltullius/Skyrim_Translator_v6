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

    private sealed record RowContext(string? Rec, string? Edid);

    private string? GetRecForId(long id)
    {
        if (_rowContextById == null)
        {
            return null;
        }

        return _rowContextById.TryGetValue(id, out var ctx) ? ctx.Rec : null;
    }

    private string? GetEdidForId(long id)
    {
        if (_rowContextById == null)
        {
            return null;
        }

        return _rowContextById.TryGetValue(id, out var ctx) ? ctx.Edid : null;
    }

    private string? GetDialogueContextWindowForId(long id)
    {
        if (_dialogueContextWindowById == null)
        {
            return null;
        }

        return _dialogueContextWindowById.TryGetValue(id, out var ctx) ? ctx : null;
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

        var laneGate = lane == RequestLane.VeryLong ? _veryLongRequestGate : null;
        var gate = _generateContentGate;
        if (gate == null && laneGate == null)
        {
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
                await WaitForAdaptiveConcurrencySlotAsync(cancellationToken);
                adaptiveAcquired = true;
                var response = await _gemini.GenerateContentAsync(apiKey, modelName, request, cancellationToken);
                RegisterAdaptiveRequestSuccess();
                return response;
            }
            finally
            {
                if (adaptiveAcquired)
                {
                    ReleaseAdaptiveConcurrencySlot();
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

        var laneGate = lane == RequestLane.VeryLong ? _veryLongRequestGate : null;
        var gate = _generateContentGate;
        if (gate == null && laneGate == null)
        {
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
                await WaitForAdaptiveConcurrencySlotAsync(cancellationToken);
                adaptiveAcquired = true;
                var response = await _gemini.GenerateContentCandidatesAsync(apiKey, modelName, request, cancellationToken);
                RegisterAdaptiveRequestSuccess();
                return response;
            }
            finally
            {
                if (adaptiveAcquired)
                {
                    ReleaseAdaptiveConcurrencySlot();
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

        var gate = _generateContentGate;
        if (gate == null)
        {
            return await _gemini.CountTokensAsync(apiKey, modelName, text, cancellationToken);
        }

        await gate.WaitAsync(cancellationToken);
        var adaptiveAcquired = false;
        try
        {
            await WaitForAdaptiveConcurrencySlotAsync(cancellationToken);
            adaptiveAcquired = true;
            var tokenCount = await _gemini.CountTokensAsync(apiKey, modelName, text, cancellationToken);
            RegisterAdaptiveRequestSuccess();
            return tokenCount;
        }
        finally
        {
            if (adaptiveAcquired)
            {
                ReleaseAdaptiveConcurrencySlot();
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
