namespace XTranslatorAi.Core.Translation;

public static class GeminiTranslationPolicy
{
    public static bool SupportsMultipleCandidates(string modelName)
        => GeminiModelPolicy.SupportsMultipleCandidates(modelName);

    public static GeminiThinkingConfig? GetLowThinkingConfigForTranslation(string modelName)
        => GeminiModelPolicy.GetLowThinkingConfigForTranslation(modelName);

    public static GeminiThinkingConfig? GetThinkingConfigForTranslation(string modelName)
        => GeminiModelPolicy.GetThinkingConfigForTranslation(modelName);

    public static double? GetTemperatureForTranslation(string modelName, double requestedTemperature)
        => GeminiModelPolicy.GetTemperatureForTranslation(modelName, requestedTemperature);
}
