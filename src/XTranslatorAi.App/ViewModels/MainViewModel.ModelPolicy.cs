using System;
using System.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private const double TranslationTemperature = 0.1;

    public string EffectiveGeminiTranslationConfigSummary => BuildGeminiTranslationConfigSummary();

    public string EffectiveGeminiTranslationConfigToolTip => BuildGeminiTranslationConfigToolTip();

    public string SelectedModelCostSummary => BuildSelectedModelCostSummary();

    private string BuildGeminiTranslationConfigSummary()
    {
        var model = (SelectedModel ?? "").Trim();
        var maxOut = ComputeMaxOutputTokens(model);

        var temp = GeminiTranslationPolicy.GetTemperatureForTranslation(model, TranslationTemperature);
        var tempText = temp is null ? "default" : temp.Value.ToString("0.###");

        var thinking = GeminiTranslationPolicy.GetThinkingConfigForTranslation(model);
        var thinkingText = DescribeThinkingConfig(thinking);

        var maxOutText = MaxOutputTokensOverride > 0 ? $"override({maxOut})" : $"auto({maxOut})";

        return $"GenCfg: temp={tempText}, think={thinkingText}, maxOut={maxOutText}";
    }

    private string BuildGeminiTranslationConfigToolTip()
    {
        var model = (SelectedModel ?? "").Trim();
        var sb = new StringBuilder();
        sb.AppendLine("Translation request config (effective):");
        AppendModelLine(sb, model);

        var temp = GeminiTranslationPolicy.GetTemperatureForTranslation(model, TranslationTemperature);
        AppendTemperatureLine(sb, temp);

        var thinking = GeminiTranslationPolicy.GetThinkingConfigForTranslation(model);
        AppendThinkingLine(sb, thinking);

        var modelLimit = GetModelOutputTokenLimit(model);
        var maxOut = ComputeMaxOutputTokens(model);
        AppendMaxOutputLine(sb, maxOut, MaxOutputTokensOverride > 0, modelLimit);
        AppendTranslationConfigNote(sb);

        return sb.ToString().TrimEnd();
    }

    private string BuildSelectedModelCostSummary()
    {
        return "";
    }

    private static void AppendModelLine(StringBuilder sb, string model)
    {
        sb.Append("Model: ");
        sb.AppendLine(string.IsNullOrWhiteSpace(model) ? "(none)" : model);
    }

    private static void AppendTemperatureLine(StringBuilder sb, double? temperature)
    {
        sb.Append("Temperature: ");
        if (temperature is null)
        {
            sb.AppendLine("API default (field omitted)");
            return;
        }

        sb.Append(temperature.Value.ToString("0.###"));
        sb.AppendLine(" (field sent)");
    }

    private static void AppendThinkingLine(StringBuilder sb, GeminiThinkingConfig? thinking)
    {
        sb.Append("Thinking: ");
        if (thinking is null)
        {
            sb.AppendLine("API default (field omitted)");
        }
        else if (thinking.ThinkingBudget == 0)
        {
            sb.AppendLine("disabled (budget=0)");
        }
        else if (!string.IsNullOrWhiteSpace(thinking.ThinkingLevel))
        {
            sb.Append("level=");
            sb.AppendLine(thinking.ThinkingLevel);
        }
        else if (thinking.ThinkingBudget is not null)
        {
            sb.Append("budget=");
            sb.AppendLine(thinking.ThinkingBudget.Value.ToString());
        }
        else
        {
            sb.AppendLine("custom");
        }
    }

    private static void AppendMaxOutputLine(StringBuilder sb, int maxOutputTokens, bool isOverride, int? modelLimit)
    {
        sb.Append("Max output tokens: ");
        sb.Append(maxOutputTokens);
        sb.Append(isOverride ? " (override)" : " (auto)");
        if (modelLimit is > 0)
        {
            sb.Append(" | model limit=");
            sb.Append(modelLimit.Value);
        }
        sb.AppendLine();
    }

    private static void AppendTranslationConfigNote(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("Note:");
        sb.AppendLine("- Gemini 3: temperature is omitted to use API defaults.");
        sb.AppendLine("- Gemini 3 Flash: thinkingConfig is omitted to use API defaults.");
        sb.AppendLine("- Gemini 2.5 Flash Lite: temperature/thinkingConfig are omitted to use API defaults.");
    }

    private static string DescribeThinkingConfig(GeminiThinkingConfig? thinking)
    {
        if (thinking is null)
        {
            return "default";
        }
        if (thinking.ThinkingBudget == 0)
        {
            return "off";
        }
        if (!string.IsNullOrWhiteSpace(thinking.ThinkingLevel))
        {
            return thinking.ThinkingLevel.Trim();
        }
        if (thinking.ThinkingBudget is not null)
        {
            return $"budget={thinking.ThinkingBudget.Value}";
        }

        return "custom";
    }

    private int ComputeMaxOutputTokens(string modelName)
    {
        var selectedModel = (modelName ?? "").Trim();

        var maxOut = GetDefaultMaxOutputTokensForModel(selectedModel);
        if (_modelInfoByName.TryGetValue(selectedModel, out var modelInfo) && modelInfo.OutputTokenLimit is > 0)
        {
            maxOut = Math.Clamp(modelInfo.OutputTokenLimit.Value, 256, 65536);
        }

        if (MaxOutputTokensOverride > 0)
        {
            maxOut = Math.Clamp(MaxOutputTokensOverride, 256, 65536);
            if (_modelInfoByName.TryGetValue(selectedModel, out var limitedModel) && limitedModel.OutputTokenLimit is > 0)
            {
                maxOut = Math.Min(maxOut, limitedModel.OutputTokenLimit.Value);
            }
        }

        return maxOut;
    }

    private int? GetModelOutputTokenLimit(string modelName)
    {
        var selectedModel = (modelName ?? "").Trim();
        if (_modelInfoByName.TryGetValue(selectedModel, out var modelInfo) && modelInfo.OutputTokenLimit is > 0)
        {
            return modelInfo.OutputTokenLimit.Value;
        }

        return null;
    }

    private static int GetDefaultMaxOutputTokensForModel(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return 8192;
        }

        // Default to a larger cap for modern Gemini models to avoid MAX_TOKENS truncation on long book texts.
        // Actual output is still bounded by content + prompt rules; this is just a ceiling.
        if (modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
        {
            return 65536;
        }

        return 8192;
    }

    partial void OnSelectedModelChanged(string value)
    {
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigSummary));
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigToolTip));
        OnPropertyChanged(nameof(SelectedModelCostSummary));
    }

    partial void OnMaxOutputTokensOverrideChanged(int value)
    {
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigSummary));
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigToolTip));
    }
}
