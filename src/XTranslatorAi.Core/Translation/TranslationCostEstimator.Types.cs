using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace XTranslatorAi.Core.Translation;

public sealed record TranslationCostEstimate(
    string ScopeLabel,
    string ModelName,
    int ItemCount,
    int BatchSize,
    int MaxCharsPerBatch,
    int MaxOutputTokens,
    long TotalSourceChars,
    long TotalMaskedChars,
    int BatchRequestCount,
    int TextRequestCount,
    long SystemPromptTokens,
    long InputTokensBatchPrompts,
    long InputTokensTextPrompts,
    OutputTokenEstimate OutputTokens,
    IReadOnlyList<ModelCostEstimate> CostEstimates
)
{
    public string ToHumanReadableString()
    {
        static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine($"범위: {ScopeLabel}");
        sb.AppendLine($"모델: {ModelName}");
        sb.AppendLine($"항목 수: {N(ItemCount)}");
        sb.AppendLine($"설정: batch={BatchSize}, maxChars={MaxCharsPerBatch}, maxOut={MaxOutputTokens}");
        sb.AppendLine($"요청 수: {N(BatchRequestCount + TextRequestCount)} (배치={N(BatchRequestCount)}, 텍스트/청크={N(TextRequestCount)})");
        sb.AppendLine($"문자 수: 원문={N(TotalSourceChars)}, 마스킹+용어집={N(TotalMaskedChars)}");
        sb.AppendLine(
            $"입력 토큰(countTokens): 배치={N(InputTokensBatchPrompts)}, 텍스트/청크={N(InputTokensTextPrompts)}, 합계={N(InputTokensBatchPrompts + InputTokensTextPrompts)}"
        );
        sb.AppendLine($"시스템 프롬프트 토큰(countTokens): {N(SystemPromptTokens)}");
        sb.AppendLine(
            OutputTokens.UsedSample
                ? $"예상 출력 토큰(샘플 기반): {N(OutputTokens.Point)} (범위 {N(OutputTokens.Low)} ~ {N(OutputTokens.High)})"
                : $"예상 출력 토큰(휴리스틱): {N(OutputTokens.Point)} (범위 {N(OutputTokens.Low)} ~ {N(OutputTokens.High)})"
        );

        if (OutputTokens.BatchRatio is > 0 || OutputTokens.TextRatio is > 0)
        {
            sb.AppendLine(
                $"샘플 비율(출력/입력): 배치={OutputTokens.BatchRatio?.ToString("0.###", CultureInfo.InvariantCulture) ?? "해당없음"}, 텍스트={OutputTokens.TextRatio?.ToString("0.###", CultureInfo.InvariantCulture) ?? "해당없음"}"
            );
        }

        AppendCostEstimates(sb);
        AppendNotes(sb);
        return sb.ToString();
    }

    private void AppendCostEstimates(StringBuilder sb)
    {
        if (CostEstimates.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("비용 추정(USD, Google 공식 가격표 기준):");
        foreach (var c in CostEstimates)
        {
            sb.AppendLine($"- {c.ModelName}");
            sb.AppendLine(
                $"  프롬프트 캐시 사용: ${c.TotalCostUsdLowWithPromptCache:0.###} ~ ${c.TotalCostUsdHighWithPromptCache:0.###} (입력 ${c.InputCostUsdWithPromptCache:0.###} + 출력)"
            );
            sb.AppendLine(
                $"  프롬프트 캐시 미사용: ${c.TotalCostUsdLowWithoutPromptCache:0.###} ~ ${c.TotalCostUsdHighWithoutPromptCache:0.###} (입력 ${c.InputCostUsdWithoutPromptCache:0.###} + 출력)"
            );
            sb.AppendLine(
                $"  배치 API(이론값): ${c.TotalCostUsdLowBatch:0.###} ~ ${c.TotalCostUsdHighBatch:0.###} (프롬프트 캐시 미적용)"
            );
        }
    }

    private static void AppendNotes(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("참고:");
        sb.AppendLine("- 이 값은 1회 성공 기준(재시도/실패 비용 미포함)이며, 특히 초장문에서 실패/재시도가 많으면 실제 비용은 더 증가할 수 있습니다.");
        sb.AppendLine("- 배치 API는 가격표에 따라 저렴하지만, 현재 앱은 실시간 번역(일반 API)을 사용합니다. 배치 API 비용은 참고용입니다.");
        sb.AppendLine("- 출력 가격에는 thinking tokens가 포함되며, 실제 출력 토큰은 단순 텍스트 길이보다 더 커질 수 있습니다.");
    }
}

public sealed record OutputTokenEstimate(
    long Low,
    long High,
    long Point,
    double? BatchRatio,
    double? TextRatio,
    bool UsedSample
);

public readonly record struct GeminiPricing(
    double InputUsdPer1M,
    double OutputUsdPer1M,
    double BatchInputUsdPer1M,
    double BatchOutputUsdPer1M,
    double CacheUsdPer1M,
    double CacheStorageUsdPer1MPerHour,
    bool BatchSupportsContextCaching
);

public sealed record ModelCostEstimate(
    string ModelName,
    double InputUsdPer1M,
    double OutputUsdPer1M,
    double BatchInputUsdPer1M,
    double BatchOutputUsdPer1M,
    double CacheUsdPer1M,
    double CacheStorageUsdPer1MPerHour,
    double PromptCacheTtlHours,
    double InputCostUsdWithPromptCache,
    double InputCostUsdWithoutPromptCache,
    double OutputCostUsdLow,
    double OutputCostUsdHigh,
    double TotalCostUsdLowWithPromptCache,
    double TotalCostUsdHighWithPromptCache,
    double TotalCostUsdLowWithoutPromptCache,
    double TotalCostUsdHighWithoutPromptCache,
    double BatchInputCostUsd,
    double BatchOutputCostUsdLow,
    double BatchOutputCostUsdHigh,
    double TotalCostUsdLowBatch,
    double TotalCostUsdHighBatch
);

internal sealed record SampleRatios(double? BatchRatio, double? TextRatio)
{
    public bool HasAny => BatchRatio is > 0 || TextRatio is > 0;
}
