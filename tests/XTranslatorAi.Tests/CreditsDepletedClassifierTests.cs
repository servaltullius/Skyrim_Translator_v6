using System;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Gemini answers spent prepaid credits with 402 and the status RESOURCE_EXHAUSTED; it was read as a rate limit
/// (E202 "요청이 너무 많습니다"), retried and reported as one, while no waiting helps.
/// </summary>
public class CreditsDepletedClassifierTests
{
    private const string Message =
        "GenerateContent failed: HTTP 402 Payment Required. {\n  \"error\": {\n    \"code\": 402,\n"
        + "    \"message\": \"Your prepayment credits are depleted. Please go to AI Studio at https://ai.studio/projects to manage your project and billing.\",\n"
        + "    \"status\": \"RESOURCE_EXHAUSTED\"\n  }\n}";

    [Fact]
    public void Classify_MapsGemini402_ToCreditsDepleted()
    {
        var http = new GeminiHttpException("generateContent", 402, "Payment Required", null, Message);

        var live = UserFacingErrorClassifier.Classify(new InvalidOperationException("Translate batch failed: " + Message, http));
        var stored = UserFacingErrorClassifier.ClassifyErrorMessage("InvalidOperationException: Translate batch failed: " + Message);

        Assert.Equal("E205", live.Code);
        Assert.Equal("E205", stored.Code);
        Assert.Contains("크레딧", live.Message);
        Assert.Contains("AI Studio", live.Message);
    }
}
