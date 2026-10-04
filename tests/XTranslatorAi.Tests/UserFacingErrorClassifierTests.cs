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

    // Row errors keep the whole message chain, including row ids; "1429" and "id: 401" are not HTTP statuses.
    [Theory]
    [InlineData("Translate batch failed: Model output missing id: 1429", "E320")]
    [InlineData("Translate batch failed: Model output missing id: 401", "E320")]
    [InlineData("GenerateContent failed: HTTP 429 Too Many Requests. {\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}", "E202")]
    [InlineData("GenerateContent failed: HTTP 400 Bad Request. {\"error\":{\"message\":\"API key not valid. Please pass a valid API key.\"}}", "E201")]
    public void ClassifyErrorMessage_ReadsStatusesOnlyWhereTheyAreStatuses(string message, string code)
        => Assert.Equal(code, UserFacingErrorClassifier.ClassifyErrorMessage(message).Code);

    [Theory]
    [InlineData("{\"error\":{\"code\":400,\"message\":\"API key expired. Please renew the API key.\",\"status\":\"INVALID_ARGUMENT\"}}", "E201")]
    [InlineData("{\"error\":{\"code\":400,\"message\":\"Request contains an invalid argument.\",\"status\":\"INVALID_ARGUMENT\"}}", "E299")]
    public void Classify_TellsAnInvalidKeyFromOtherBadRequests(string body, string code)
    {
        var ex = new GeminiHttpException("GenerateContent", 400, "Bad Request", null, $"GenerateContent failed: HTTP 400 Bad Request. {body}");

        Assert.Equal(code, UserFacingErrorClassifier.Classify(ex).Code);
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
