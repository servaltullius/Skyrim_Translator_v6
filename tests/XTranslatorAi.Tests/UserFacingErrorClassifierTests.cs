using System;
using System.Net.Http;
using System.Threading.Tasks;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class UserFacingErrorClassifierTests
{
    [Fact]
    public void Classify_MapsGemini429_ToRateLimit()
    {
        var ex = new GeminiHttpException(
            operation: "GenerateContent",
            statusCode: 429,
            reasonPhrase: "Too Many Requests",
            retryAfter: TimeSpan.FromSeconds(10),
            message: "GenerateContent failed: HTTP 429"
        );

        var error = UserFacingErrorClassifier.Classify(ex);
        Assert.Equal("E202", error.Code);
        Assert.True(error.DetailsInApiLogs);
    }

    [Fact]
    public void Classify_MapsMaxTokens_ToGuidance()
    {
        var ex = new GeminiException("GenerateContent: finishReason=MAX_TOKENS (output truncated).");

        var error = UserFacingErrorClassifier.Classify(ex);
        Assert.Equal("E310", error.Code);
        Assert.True(error.DetailsInApiLogs);
    }

    [Fact]
    public void Classify_MapsNetwork_ToGuidance()
    {
        var ex = new HttpRequestException("No route to host");

        var error = UserFacingErrorClassifier.Classify(ex);
        Assert.Equal("E211", error.Code);
        Assert.True(error.DetailsInApiLogs);
    }

    [Fact]
    public void Classify_MapsTimeout_ToGuidance()
    {
        var ex = new TaskCanceledException("timed out");

        var error = UserFacingErrorClassifier.Classify(ex);
        Assert.Equal("E210", error.Code);
        Assert.True(error.DetailsInApiLogs);
    }

    [Fact]
    public void Classify_MapsGemini5xx_ToServerError()
    {
        var ex = new GeminiHttpException(
            operation: "GenerateContent",
            statusCode: 503,
            reasonPhrase: "Service Unavailable",
            retryAfter: TimeSpan.FromSeconds(5),
            message: "GenerateContent failed: HTTP 503 Service Unavailable"
        );

        var error = UserFacingErrorClassifier.Classify(ex);
        Assert.Equal("E203", error.Code);
        Assert.True(error.DetailsInApiLogs);
    }

    [Fact]
    public void Classify_MapsTokenValidation_ToGuidance()
    {
        var ex = new InvalidOperationException("Missing token in translation: __XT_PH_0001__");

        var error = UserFacingErrorClassifier.Classify(ex);
        Assert.Equal("E330", error.Code);
        Assert.False(error.DetailsInApiLogs);
    }

    [Fact]
    public void Classify_MapsUnknown_ToFallback()
    {
        var error = UserFacingErrorClassifier.Classify(new Exception("boom"));
        Assert.Equal("E999", error.Code);
    }

    [Fact]
    public void ClassifyErrorMessage_MapsUnauthorized_ToKeyGuidance()
    {
        var error = UserFacingErrorClassifier.ClassifyErrorMessage("GenerateContent failed: statusCode=401 Unauthorized");
        Assert.Equal("E201", error.Code);
        Assert.True(error.DetailsInApiLogs);
    }
}
