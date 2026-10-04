using System;
using System.Threading;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private readonly record struct LongTextChunkContext(
        string ApiKey,
        string ModelName,
        string SystemPrompt,
        PromptCache? PromptCache,
        string SourceLang,
        string TargetLang,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) Row,
        double Temperature,
        int MaxOutputTokens,
        int MaxRetries,
        string? StyleHint,
        CancellationToken CancellationToken,
        string? SourceReference = null
    );

    private static int GetMaxTokensPerChunk(string modelName, string targetLang)
    {
        var normalizedModel = modelName?.Trim() ?? "";
        if (normalizedModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedModel = normalizedModel["models/".Length..];
        }

        var isGemini3 = normalizedModel.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
        var isCjk = IsCjkLanguage(targetLang);

        return isGemini3
            ? isCjk ? 24 : 32
            : isCjk ? 30 : 40;
    }

    internal static bool IsCjkLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim().ToLowerInvariant();
        if (s is "korean" or "japanese" or "chinese" or "zh" or "ja" or "ko")
        {
            return true;
        }

        return s.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    internal static int GetLongTextTargetOutputTokens(int maxOutputTokens)
    {
        if (maxOutputTokens <= 0)
        {
            return 2048;
        }

        // Keep well below the model's max output tokens to reduce MAX_TOKENS truncation.
        var target = (int)Math.Floor(maxOutputTokens * 0.35);
        target = Math.Clamp(target, 512, 6000);

        // Leave some headroom for the model to finish naturally (and for sentinel / tokens).
        var headroom = Math.Min(512, Math.Max(128, maxOutputTokens / 10));
        target = Math.Min(target, Math.Max(256, maxOutputTokens - headroom));

        return Math.Max(256, target);
    }

    private static string? GuessStyleHint(string sourceText, string? rec)
        => TranslationStyleHints.Get(sourceText, rec);
}
