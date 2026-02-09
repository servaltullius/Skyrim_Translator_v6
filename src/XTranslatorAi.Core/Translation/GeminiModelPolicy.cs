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

    internal static GeminiThinkingConfig? GetThinkingConfigForTranslation(string modelName)
    {
        var m = NormalizeModelName(modelName);
        if (string.IsNullOrWhiteSpace(m))
        {
            return null;
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
        // For Gemini 3, Google recommends using the model default temperature (1.0) to avoid looping or degraded
        // performance from explicitly setting low values. We omit the field to use API defaults.
        var m = NormalizeModelName(modelName);
        if (IsGemini3(m))
        {
            return null;
        }

        if (IsGemini25FlashLite(m))
        {
            return null;
        }

        return temperature;
    }

    internal static bool IsGemini3FlashPreview(string modelName)
    {
        var m = NormalizeModelName(modelName);
        return IsGemini3Flash(m) && m.Contains("-preview", StringComparison.OrdinalIgnoreCase);
    }
}
