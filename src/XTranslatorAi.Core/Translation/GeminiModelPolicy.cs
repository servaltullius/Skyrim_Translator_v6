using System;

namespace XTranslatorAi.Core.Translation;

internal static class GeminiModelPolicy
{
    internal static string NormalizeModelName(string? modelName)
    {
        var m = modelName?.Trim() ?? "";
        if (m.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            m = m.Substring("models/".Length);
        }
        return m;
    }

    private static bool IsGemini3(string normalizedModelName)
    {
        return normalizedModelName.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGemini25Flash(string normalizedModelName)
    {
        return normalizedModelName.StartsWith("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGemini25FlashLite(string normalizedModelName)
    {
        return IsGemini25Flash(normalizedModelName)
            && normalizedModelName.Contains("-lite", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGemini3Flash(string normalizedModelName)
    {
        return IsGemini3(normalizedModelName) && normalizedModelName.Contains("-flash", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGemini3FlashLite(string normalizedModelName)
    {
        return IsGemini3Flash(normalizedModelName) && normalizedModelName.Contains("-lite", StringComparison.OrdinalIgnoreCase);
    }

    internal static GeminiThinkingConfig? GetThinkingConfigForTranslation(string modelName)
    {
        var m = NormalizeModelName(modelName);
        if (string.IsNullOrWhiteSpace(m))
        {
            return null;
        }

        // Exact stable IDs match the recorded LOTD web comparison. This sets the
        // same thinking level; it does not establish app-level translation quality.
        if (m.Equals(GeminiModelCatalog.DefaultModel, StringComparison.OrdinalIgnoreCase))
            return new GeminiThinkingConfig(ThinkingBudget: null, ThinkingLevel: "low");
        if (m.Equals(GeminiModelCatalog.LowCostModel, StringComparison.OrdinalIgnoreCase))
            return new GeminiThinkingConfig(ThinkingBudget: null, ThinkingLevel: "minimal");

        // Preserve the existing Flash-Lite translation setting. Quality must be
        // evaluated separately; high thinking is not proof of a better translation.
        if (IsGemini3FlashLite(m))
        {
            return new GeminiThinkingConfig(ThinkingBudget: null, ThinkingLevel: "high");
        }

        // Gemini 3 Flash models default to dynamic "thinking".
        // For testing model-default behavior (and to let the API choose), omit thinkingConfig.
        if (IsGemini3Flash(m))
        {
            return null;
        }

        // Gemini 2.5 Flash Lite: use API defaults for translation (omit thinkingConfig).
        if (IsGemini25FlashLite(m))
        {
            return null;
        }

        // Gemini 3: keep "low" thinking for throughput (and because some variants don't support "minimal").
        if (IsGemini3(m))
        {
            return new GeminiThinkingConfig(ThinkingBudget: null, ThinkingLevel: "low");
        }

        // Gemini 2.5 Flash: disable dynamic thinking for translation.
        if (IsGemini25Flash(m))
        {
            return new GeminiThinkingConfig(ThinkingBudget: 0);
        }

        return null;
    }

    internal static double? GetTemperatureForTranslation(string modelName, double temperature)
    {
        // Gemini 3.6+/3.5 Flash-Lite deprecate sampling controls. Keep them omitted
        // throughout Gemini 3; this also preserves the older models' API defaults.
        // https://ai.google.dev/gemini-api/docs/latest-model (2026-09-28)
        var m = NormalizeModelName(modelName);
        // Unversioned aliases may change their target. Unknown model capabilities
        // must not inherit legacy sampling controls merely because they are not 3.x.
        return SupportsLegacySamplingControls(m) && !IsGemini25FlashLite(m) ? temperature : null;
    }

    internal static GeminiThinkingConfig? GetLowThinkingConfigForTranslation(string modelName)
    {
        var model = NormalizeModelName(modelName);
        if (model.Equals(GeminiModelCatalog.LowCostModel, StringComparison.OrdinalIgnoreCase))
            return new GeminiThinkingConfig(ThinkingBudget: null, ThinkingLevel: "minimal");
        if (IsGemini3(model))
        {
            // Gemini 3.8 supports low/medium/high; minimal and budget=0 are invalid.
            return new GeminiThinkingConfig(ThinkingBudget: null, ThinkingLevel: "low");
        }
        if (IsGemini25Flash(model))
        {
            return new GeminiThinkingConfig(ThinkingBudget: 0);
        }
        return null;
    }

    /// <summary>
    /// Enables candidateCount only for known legacy models. New models and mutable
    /// aliases use API defaults until their capabilities have been verified.
    /// </summary>
    internal static bool SupportsMultipleCandidates(string modelName)
    {
        return SupportsLegacySamplingControls(NormalizeModelName(modelName));
    }

    private static bool SupportsLegacySamplingControls(string modelName)
        => modelName.ToLowerInvariant() is "gemini-2.5-flash" or "gemini-2.5-flash-lite" or "gemini-2.5-pro"
            or "gemini-2.0-flash" or "gemini-2.0-flash-001" or "gemini-2.0-flash-lite" or "gemini-2.0-flash-lite-001";
}
