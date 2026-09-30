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

    public bool SelectedModelSupportsMultipleCandidates
        => GeminiTranslationPolicy.SupportsMultipleCandidates(SelectedModel ?? "");

    public bool IsRiskyCandidateCountEnabled
        => SelectedModelSupportsMultipleCandidates && EnableRiskyCandidateRerank;

    public int EffectiveRiskyCandidateCount
        => IsRiskyCandidateCountEnabled ? Math.Clamp(RiskyCandidateCount, 2, 8) : 1;

    public string EffectiveRiskyCandidateSummary
        => SelectedModelSupportsMultipleCandidates
            ? $"기본 모델 적용: {EffectiveRiskyCandidateCount}개 (위험 문장)"
            : $"기본 모델 적용: 1개 · 다중후보 미지원/미확인 (저장값 {RiskyCandidateCount}개)";

    private string BuildGeminiTranslationConfigSummary()
    {
        var model = (SelectedModel ?? "").Trim();
        var maxOut = ComputeMaxOutputTokens(model);

        var temp = GeminiTranslationPolicy.GetTemperatureForTranslation(model, TranslationTemperature);
        var tempText = temp is null ? "default" : temp.Value.ToString("0.###");

        var thinking = GeminiTranslationPolicy.GetThinkingConfigForTranslation(model);
        var thinkingText = DescribeThinkingConfig(thinking);

        var maxOutText = MaxOutputTokensOverride > 0 ? $"override({maxOut})" : $"auto({maxOut})";
        if (EnableAdaptiveOutputBudget) maxOutText = $"adaptive(상한 {maxOut})";

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
        if (EnableAdaptiveOutputBudget)
            sb.AppendLine("실험 옵션: 실제 요청 상한은 원문 길이/보호 토큰 수에 따라 위 상한 이하로 조절됩니다.");
        AppendTranslationConfigNote(sb);

        return sb.ToString().TrimEnd();
    }

    private string BuildSelectedModelCostSummary()
    {
        var model = (SelectedModel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            return "";
        }

        if (!GeminiPricingTable.TryGetPricing(model, out var pricing))
        {
            return "Pricing: unknown model";
        }

        return $"표준 텍스트 요금 · In ${pricing.InputUsdPer1M}/1M · Out ${pricing.OutputUsdPer1M}/1M · Cache ${pricing.CacheUsdPer1M}/1M · {DateTime.UtcNow:yyyy-MM-dd} UTC";
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
        sb.AppendLine("- Gemini 3: sampling controls are omitted; newer models deprecate temperature/topP/topK.");
        sb.AppendLine("- Latest aliases and unrecognized models omit sampling controls and multiple-candidate requests.");
        sb.AppendLine("- Gemini 3.8 Flash: low; Gemini 3.1 Flash-Lite: minimal (exact stable IDs).");
        sb.AppendLine("- Other Gemini 3 Flash models use API defaults; other Flash-Lite models retain high.");
        sb.AppendLine("- Gemini 2.5 Flash Lite: temperature/thinkingConfig are omitted to use API defaults.");
        sb.AppendLine("- Gemini 3.8 Flash supports low/medium/high thinking, not minimal or budget=0.");
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
        OnPropertyChanged(nameof(SelectedModelSupportsMultipleCandidates));
        OnPropertyChanged(nameof(IsRiskyCandidateCountEnabled));
        OnPropertyChanged(nameof(EffectiveRiskyCandidateCount));
        OnPropertyChanged(nameof(EffectiveRiskyCandidateSummary));
    }

    partial void OnMaxOutputTokensOverrideChanged(int value)
    {
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigSummary));
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigToolTip));
    }

    partial void OnEnableAdaptiveOutputBudgetChanged(bool value)
    {
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigSummary));
        OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigToolTip));
    }
}
