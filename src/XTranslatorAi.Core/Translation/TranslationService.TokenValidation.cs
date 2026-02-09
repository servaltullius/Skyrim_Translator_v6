using System;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
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

    private static void ValidateNotTruncatedOrOmitted(string inputText, string outputText, string context)
    {
        SplitByTokens(inputText, out var inputTexts, out _);
        SplitByTokens(outputText, out var outputTexts, out _);

        if (inputTexts.Count != outputTexts.Count)
        {
            return;
        }

        for (var i = 0; i < inputTexts.Count; i++)
        {
            var inLen = CountLettersOrDigits(inputTexts[i]);
            if (inLen < 240)
            {
                continue;
            }

            var outLen = CountLettersOrDigits(outputTexts[i]);

            // The output can be shorter (e.g., English -> Korean), but it should not collapse
            // a large segment into a tiny summary or empty string.
            var minRatio = inLen >= 800 ? 0.20 : 0.16;
            var minAbs = inLen >= 800 ? 120 : 60;
            var required = Math.Max(minAbs, (int)Math.Ceiling(inLen * minRatio));
            if (outLen < required)
            {
                throw new InvalidOperationException(
                    $"Translation appears to omit content for {context} (segment {i}): input={inLen}, output={outLen}."
                );
            }
        }
    }

    private static int CountLettersOrDigits(string s)
    {
        var count = 0;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
            {
                count++;
            }
        }
        return count;
    }
}

