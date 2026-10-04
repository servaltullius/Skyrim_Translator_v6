using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private async Task<long> CountTokensForManyTextsAsync(
        string apiKey,
        string modelName,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken
    )
    {
        if (texts.Count == 0)
        {
            return 0;
        }

        // CountTokens has a very large input limit (1M tokens), but keep requests smaller to avoid failures.
        const int maxConcatChars = 200_000;
        var total = 0L;
        var sb = new StringBuilder(capacity: Math.Min(maxConcatChars, 4096));
        var currentLen = 0;

        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = texts[i] ?? "";
            if (t.Length == 0)
            {
                continue;
            }

            if (currentLen > 0 && currentLen + t.Length + 2 > maxConcatChars)
            {
                total += await SafeCountTokensAsync(apiKey, modelName, sb.ToString(), cancellationToken);
                sb.Clear();
                currentLen = 0;
            }

            sb.Append(t);
            sb.Append("\n\n");
            currentLen += t.Length + 2;
        }

        if (currentLen > 0)
        {
            total += await SafeCountTokensAsync(apiKey, modelName, sb.ToString(), cancellationToken);
        }

        return total;
    }

    private async Task<long> SafeCountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            return await _gemini.CountTokensAsync(apiKey, modelName, text, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsInvalidApiKey(ex))
        {
            // An estimate made entirely from character counts used to "succeed" with a wrong key.
            throw;
        }
        catch
        {
            // Every failure (rate limit, server error) used to fall back silently while the result still said
            // "countTokens"; the estimate now says how many counts were guessed.
            Interlocked.Increment(ref _countTokensFallbacks);
            return EstimateTokensFromCharacters(text);
        }
    }

    private int _countTokensFallbacks;

    // About 4 characters per token for ASCII and fewer for Korean: dividing the Korean meta prompt by 4 undercounted
    // the system prompt that every request repeats.
    private static long EstimateTokensFromCharacters(string text)
    {
        var ascii = 0;
        foreach (var c in text)
        {
            if (c < 0x80)
            {
                ascii++;
            }
        }

        return Math.Max(1, (long)Math.Ceiling(ascii / 4.0 + (text.Length - ascii) / 1.5));
    }

    private static bool IsInvalidApiKey(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is GeminiHttpException http && GeminiErrorKinds.IsInvalidApiKey(http))
            {
                return true;
            }
        }

        return false;
    }
}
