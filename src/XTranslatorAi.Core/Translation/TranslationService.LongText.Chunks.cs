using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task<string> TranslateChunkWithAdaptiveSplittingAsync(
        LongTextChunkContext chunkContext,
        string chunkText,
        int chunkChars,
        int minChunkChars
    )
    {
        chunkContext.CancellationToken.ThrowIfCancellationRequested();

        var maxTokensPerChunk = GetMaxTokensPerChunk(chunkContext.ModelName, chunkContext.TargetLang);
        var tokenCount = TranslationConstants.XtTokenRegex.Matches(chunkText).Count;

        if (chunkText.Length > chunkChars || tokenCount > maxTokensPerChunk)
        {
            // Prefer splitting at [pagebreak] boundaries for structural integrity.
            var parts = TrySplitAtPagebreakBoundaries(chunkText, chunkChars, chunkContext.Row.Mask)
                        ?? TokenAwareTextSplitter.Split(chunkText, chunkChars, maxTokensPerChunk);

            if (parts.Count <= 1)
            {
                throw new InvalidOperationException($"Failed to split long text (len={chunkText.Length}, chunk={chunkChars}).");
            }

            return await TranslateChunkPartsAsync(
                chunkContext,
                parts,
                chunkChars,
                minChunkChars
            );
        }

        try
        {
            return await TranslateChunkTextAsync(chunkContext, chunkText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!MustStopRecovery(ex))
        {
            using var recovery = EnterGenerationScope(recovery: true);
            return await TranslateChunkAfterFailureAsync(
                chunkContext,
                new ChunkSplitFailureContext(
                    ChunkText: chunkText,
                    ChunkChars: chunkChars,
                    MinChunkChars: minChunkChars,
                    MaxTokensPerChunk: maxTokensPerChunk,
                    OriginalException: ex
                )
            );
        }
    }

    private async Task<string> TranslateChunkTextAsync(LongTextChunkContext chunkContext, string chunkText)
    {
        var request = CreateLongTextChunkRequestContext(chunkContext);
        // The result carries the chunk's edge whitespace (RestoreSourceEdgeWhitespace), so a split
        // within a sentence cannot silently join two words when the chunks are concatenated.
        return await TranslateTextWithSentinelAsync(
            request,
            chunkText,
            chunkContext.Row.Glossary.PromptOnlyPairs,
            new TextWithSentinelContext(
                GlossaryTokenToReplacement: chunkContext.Row.Glossary.TokenToReplacement,
                StyleHint: AppendDialogueContextToStyleHint(chunkContext.StyleHint, chunkContext.SourceReference),
                Context: "chunk",
                SourceTextForTranslationMemory: chunkContext.Row.Source
            )
        );
    }

    private TextRequestContext CreateLongTextChunkRequestContext(LongTextChunkContext chunkContext)
    {
        return new TextRequestContext(
            ApiKey: chunkContext.ApiKey,
            ModelName: chunkContext.ModelName,
            SystemPrompt: chunkContext.SystemPrompt,
            PromptCache: chunkContext.PromptCache,
            Lane: RequestLane.VeryLong,
            SourceLang: chunkContext.SourceLang,
            TargetLang: chunkContext.TargetLang,
            Temperature: chunkContext.Temperature,
            MaxOutputTokens: chunkContext.MaxOutputTokens,
            MaxRetries: chunkContext.MaxRetries,
            CancellationToken: chunkContext.CancellationToken,
            Purpose: "translate-chunk",
            Edid: GetEdidForId(chunkContext.Row.Id)
        );
    }

    private readonly record struct ChunkSplitFailureContext(
        string ChunkText,
        int ChunkChars,
        int MinChunkChars,
        int MaxTokensPerChunk,
        Exception OriginalException
    );

    private async Task<string> TranslateChunkAfterFailureAsync(LongTextChunkContext chunkContext, ChunkSplitFailureContext failure)
    {
        var chunkText = failure.ChunkText;
        var chunkChars = failure.ChunkChars;
        var minChunkChars = failure.MinChunkChars;
        var maxTokensPerChunk = failure.MaxTokensPerChunk;
        var originalException = failure.OriginalException;

        if (chunkChars <= minChunkChars)
        {
            ExceptionDispatchInfo.Capture(originalException).Throw();
        }

        var next = Math.Max(minChunkChars, chunkChars / 2);
        if (next >= chunkText.Length)
        {
            next = Math.Max(minChunkChars, chunkText.Length / 2);
        }
        if (next >= chunkText.Length)
        {
            ExceptionDispatchInfo.Capture(originalException).Throw();
        }

        var parts = TokenAwareTextSplitter.Split(chunkText, next, maxTokensPerChunk);
        if (parts.Count <= 1)
        {
            ExceptionDispatchInfo.Capture(originalException).Throw();
        }

        return await TranslateChunkPartsAsync(
            chunkContext,
            parts,
            next,
            minChunkChars
        );
    }

    private async Task<string> TranslateChunkPartsAsync(
        LongTextChunkContext chunkContext,
        IReadOnlyList<string> parts,
        int chunkChars,
        int minChunkChars
    )
    {
        if (parts.Count == 0)
        {
            return "";
        }

        var parallelism = Ctx.LongTextChunkParallelism;
        if (parallelism <= 1 || parts.Count == 1)
        {
            return await TranslateChunkPartsSequentialAsync(chunkContext, parts, chunkChars, minChunkChars);
        }

        return await TranslateChunkPartsParallelAsync(chunkContext, parts, chunkChars, minChunkChars, parallelism);
    }

    private async Task<string> TranslateChunkPartsSequentialAsync(
        LongTextChunkContext chunkContext,
        IReadOnlyList<string> parts,
        int chunkChars,
        int minChunkChars
    )
    {
        var sb = new StringBuilder(capacity: Math.Min(4096, parts.Count * 2048));
        for (var index = 0; index < parts.Count; index++)
        {
            sb.Append(
                await TranslateChunkWithAdaptiveSplittingAsync(
                    WithBookChunkReference(chunkContext, parts, index),
                    parts[index],
                    chunkChars,
                    minChunkChars
                )
            );
        }
        return sb.ToString();
    }

    private async Task<string> TranslateChunkPartsParallelAsync(
        LongTextChunkContext chunkContext,
        IReadOnlyList<string> parts,
        int chunkChars,
        int minChunkChars,
        int parallelism
    )
    {
        var results = new string[parts.Count];
        using var gate = new SemaphoreSlim(parallelism, parallelism);
        // One chunk that fails for good fails the whole row, so the chunks still waiting are not sent and the one
        // in flight is cancelled; before, every remaining chunk (and its smaller retries) was paid for nothing.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(chunkContext.CancellationToken);
        var context = chunkContext with { CancellationToken = stop.Token };
        Exception? firstFailure = null;
        var tasks = new Task[parts.Count];

        for (var i = 0; i < parts.Count; i++)
        {
            var idx = i;
            tasks[idx] = Task.Run(
                async () =>
                {
                    await gate.WaitAsync(stop.Token);
                    try
                    {
                        results[idx] = await TranslateChunkWithAdaptiveSplittingAsync(
                            WithBookChunkReference(context, parts, idx),
                            parts[idx],
                            chunkChars,
                            minChunkChars
                        );
                    }
                    catch (Exception ex) when (!stop.IsCancellationRequested)
                    {
                        Interlocked.CompareExchange(ref firstFailure, ex, null);
                        stop.Cancel();
                        throw;
                    }
                    finally
                    {
                        gate.Release();
                    }
                },
                stop.Token
            );
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch when (firstFailure != null && !chunkContext.CancellationToken.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }

        return CombineChunkPartResults(results);
    }

    private static string CombineChunkPartResults(string[] results)
    {
        var totalLen = 0;
        foreach (var r in results)
        {
            totalLen += r.Length;
        }

        var sbAll = new StringBuilder(capacity: totalLen);
        foreach (var r in results)
        {
            sbAll.Append(r);
        }
        return sbAll.ToString();
    }

    private LongTextChunkContext WithBookChunkReference(LongTextChunkContext context, IReadOnlyList<string> parts, int index)
    {
        if (!Ctx.EnableBookContext || !TranslationBookContext.IsBody(GetRecForId(context.Row.Id))) return context;
        var reference = TranslationBookContext.Build(previous: index > 0 ? parts[index - 1] : null,
            next: index + 1 < parts.Count ? parts[index + 1] : null);
        return context with { SourceReference = reference ?? context.SourceReference };
    }

    private static IReadOnlyList<string>? TrySplitAtPagebreakBoundaries(
        string chunkText,
        int chunkChars,
        MaskedText mask)
    {
        if (mask.TokenToOriginal.Count == 0)
        {
            return null;
        }

        var parts = TokenAwareTextSplitter.SplitAtPagebreaks(chunkText, chunkChars, mask.TokenToOriginal);
        return parts.Count > 1 ? parts : null;
    }
}
