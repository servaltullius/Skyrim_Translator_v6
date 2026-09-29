using System.Net;
using System.Net.Http;
using System.Text;
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

    private static GeminiGenerateContentRequest Request()
        => new(new() { new("user", new() { new("Translate") }) }, null, null, null, null);

    private sealed class CaptureLogger : IGeminiCallLogger
    {
        public List<GeminiCallLogEntry> Entries { get; } = new();
        public void Log(GeminiCallLogEntry entry) => Entries.Add(entry);
    }

    private sealed class JsonHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
