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
        var tokenCount = XtTokenRegex.Matches(chunkText).Count;

        if (chunkText.Length > chunkChars || tokenCount > maxTokensPerChunk)
        {
            var parts = TokenAwareTextSplitter.Split(chunkText, chunkChars, maxTokensPerChunk);
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
        catch (Exception ex)
        {
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
        return await TranslateTextWithSentinelAsync(
            request,
            chunkText,
            chunkContext.Row.Glossary.PromptOnlyPairs,
            new TextWithSentinelContext(
                GlossaryTokenToReplacement: chunkContext.Row.Glossary.TokenToReplacement,
                StyleHint: chunkContext.StyleHint,
                Context: "chunk",
                SourceTextForTranslationMemory: chunkContext.Row.Source
            )
        );
    }

    private static TextRequestContext CreateLongTextChunkRequestContext(LongTextChunkContext chunkContext)
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
            CancellationToken: chunkContext.CancellationToken
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

        var parallelism = _longTextChunkParallelism;
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
        foreach (var part in parts)
        {
            sb.Append(
                await TranslateChunkWithAdaptiveSplittingAsync(
                    chunkContext,
                    part,
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
        var tasks = new Task[parts.Count];

        for (var i = 0; i < parts.Count; i++)
        {
            var idx = i;
            tasks[idx] = Task.Run(
                async () =>
                {
                    await gate.WaitAsync(chunkContext.CancellationToken);
                    try
                    {
                        results[idx] = await TranslateChunkWithAdaptiveSplittingAsync(
                            chunkContext,
                            parts[idx],
                            chunkChars,
                            minChunkChars
                        );
                    }
                    finally
                    {
                        gate.Release();
                    }
                },
                chunkContext.CancellationToken
            );
        }

        await Task.WhenAll(tasks);

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
}
