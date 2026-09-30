using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public class GeminiResponseIntegrityTests
{
    [Theory]
    [InlineData("{\"promptTokenCount\":10,\"candidatesTokenCount\":4,\"thoughtsTokenCount\":30}", 34)]
    [InlineData("{\"promptTokenCount\":10,\"candidatesTokenCount\":4,\"totalTokenCount\":44}", 34)]
    [InlineData("{}", null)]
    public async Task UsageResult_ReturnsBilledOutputIncludingThinkingAndLogsOnce(string usage, int? expected)
    {
        var body = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"번역\"}]}}],\"usageMetadata\":" + usage + "}";
        using var http = new HttpClient(new JsonHandler(body));
        var logger = new CaptureLogger();
        var client = new GeminiClient(http, logger);
        var result = await client.GenerateContentWithUsageAsync("fixture-key", "gemini-3.8-flash", Request(), CancellationToken.None);
        Assert.Equal("번역", result.Text);
        Assert.Equal(expected, result.CompletionTokens);
        Assert.Equal(expected, Assert.Single(logger.Entries).CompletionTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipartResponse_UsesAllFinalPartsAndOmitsThoughts(bool multiple)
    {
        using var http = new HttpClient(new JsonHandler("""
            {"candidates":[{"finishReason":"STOP","content":{"parts":[
                {"text":"not translation","thought":true},{"text":"첫째\n"},{"text":"둘째"}]}}],
             "usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":4,"thoughtsTokenCount":3}}
            """));
        var logger = new CaptureLogger();
        var client = new GeminiClient(http, logger);
        var text = multiple
            ? (await client.GenerateContentCandidatesAsync("fixture-key", "gemini-3.8-flash", Request(), CancellationToken.None)).Single()
            : await client.GenerateContentAsync("fixture-key", "gemini-3.8-flash", Request(), CancellationToken.None);
        Assert.Equal("첫째\n둘째", text);
        Assert.Equal(7, logger.Entries.Single().CompletionTokens);
        Assert.Equal(17, logger.Entries.Single().TotalTokens);
    }

    [Theory]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SAFETY\",\"content\":{\"parts\":[{\"text\":\"partial\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[{\"text\":\"partial\"}]}}]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"summary\",\"thought\":true}]}}]}")]
    public async Task MissingOrIncompleteFinalResponse_IsRejected(string body)
    {
        using var http = new HttpClient(new JsonHandler(body));
        var client = new GeminiClient(http);
        await Assert.ThrowsAsync<GeminiException>(() => client.GenerateContentAsync("fixture-key", "gemini-3.8-flash", Request(), CancellationToken.None));
        await Assert.ThrowsAsync<GeminiException>(() => client.GenerateContentCandidatesAsync("fixture-key", "gemini-3.8-flash", Request(), CancellationToken.None));
    }

    [Theory]
    [InlineData(false, "MAX_TOKENS")]
    [InlineData(true, "MAX_TOKENS")]
    [InlineData(false, "SAFETY")]
    [InlineData(true, "SAFETY")]
    public async Task RejectedResponse_PreservesBilledUsageAndLogsFailureOnce(bool multiple, string reason)
    {
        var body = $$$"""
            {"candidates":[{"finishReason":"{{{reason}}}","content":{"parts":[{"text":"partial"}]}}],
             "usageMetadata":{"promptTokenCount":100,"candidatesTokenCount":4,"thoughtsTokenCount":30,
                              "totalTokenCount":134,"cachedContentTokenCount":80}}
            """;
        using var http = new HttpClient(new JsonHandler(body));
        var logger = new CaptureLogger();
        var client = new GeminiClient(http, logger);
        var request = Request() with { Purpose = "repair-text" };
        await Assert.ThrowsAsync<GeminiException>(async () =>
        {
            if (multiple)
                await client.GenerateContentCandidatesAsync("fixture-key", "gemini-3.8-flash", request, CancellationToken.None);
            else
                await client.GenerateContentAsync("fixture-key", "gemini-3.8-flash", request, CancellationToken.None);
        });

        var entry = Assert.Single(logger.Entries);
        Assert.False(entry.Success);
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal(reason, entry.FinishReason);
        Assert.Equal("repair-text", entry.Purpose);
        Assert.Equal(100, entry.PromptTokens);
        Assert.Equal(4, entry.OutputTokens);
        Assert.Equal(30, entry.ThoughtsTokens);
        Assert.Equal(34, entry.CompletionTokens);
        Assert.Equal(134, entry.TotalTokens);
        Assert.Equal(80, entry.CachedContentTokens);
        Assert.True(entry.CostUsd > 0);
    }

    [Theory]
    [InlineData("not JSON", HttpStatusCode.OK)]
    [InlineData("{\"error\":{\"message\":\"quota\"}}", HttpStatusCode.TooManyRequests)]
    public async Task UnknownUsage_PreservesHttpStatusWithoutInventingZeroCost(string body, HttpStatusCode status)
    {
        using var http = new HttpClient(new JsonHandler(body, status));
        var logger = new CaptureLogger();
        var client = new GeminiClient(http, logger);
        await Assert.ThrowsAnyAsync<Exception>(() => client.GenerateContentAsync(
            "fixture-key", "gemini-3.8-flash", Request(), CancellationToken.None));
        var entry = Assert.Single(logger.Entries);
        Assert.False(entry.Success);
        Assert.Equal((int)status, entry.StatusCode);
        Assert.Null(entry.PromptTokens);
        Assert.Null(entry.CompletionTokens);
        Assert.Null(entry.CostUsd);
    }

    [Fact]
    public void Purpose_IsLocalMetadataAndNeverSerializedForApi()
    {
        var json = JsonSerializer.Serialize(Request() with { Purpose = "repair-text" });
        Assert.DoesNotContain("purpose", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repair-text", json);
    }

    private static GeminiGenerateContentRequest Request()
        => new(new() { new("user", new() { new("Translate") }) }, null, null, null, null);

    private sealed class CaptureLogger : IGeminiCallLogger
    {
        public List<GeminiCallLogEntry> Entries { get; } = new();
        public void Log(GeminiCallLogEntry entry) => Entries.Add(entry);
    }

    private sealed class JsonHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
