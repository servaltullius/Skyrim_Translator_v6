using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.Core.Translation;

/// <summary>Text translation choices verified against Google's model catalog on 2026-09-28.</summary>
public static class GeminiModelCatalog
{
    public const string DefaultModel = "gemini-3.8-flash";
    public const string LowCostModel = "gemini-3.1-flash-lite";

    public static IReadOnlyList<string> PreferredModels { get; } = Array.AsReadOnly(new[]
    {
        DefaultModel, "gemini-3.5-flash-lite", LowCostModel,
        "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.5-flash",
    });

    public static IReadOnlyList<string> LowCostModels { get; } = Array.AsReadOnly(new[]
    {
        LowCostModel, "gemini-3.5-flash-lite",
    });

    public static IReadOnlyList<string> FullModels { get; } = Array.AsReadOnly(new[]
    {
        DefaultModel, "gemini-3.7-flash", "gemini-3.6-flash", "gemini-3.5-flash",
    });

    public static bool TryGetTextTranslationModelName(GeminiModel model, out string modelName)
    {
        modelName = GeminiModelPolicy.NormalizeModelName(model.Name);
        if (modelName.Length == 0
            || model.SupportedGenerationMethods == null
            || !model.SupportedGenerationMethods.Contains("generateContent", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // generateContent also serves endpoints with image/audio output, which
        // cannot satisfy the app's plain-text/JSON translation response contract.
        foreach (var marker in new[] { "-image", "-tts", "-live", "-audio", "-transcribe", "-robotics", "-omni" })
        {
            if (modelName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}
