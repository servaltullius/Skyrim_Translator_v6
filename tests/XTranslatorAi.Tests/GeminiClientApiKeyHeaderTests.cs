using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The key went into the URL query (?key=…), where HTTP debuggers, TLS-inspecting proxies and a proxy error page
/// echoed into an exception message could show it. Every call now sends it in the x-goog-api-key header.
/// </summary>
public class GeminiClientApiKeyHeaderTests
{
    private const string Key = "AIza-test-key-123456";

    [Fact]
    public async Task EveryCall_SendsTheKeyInTheHeader_NotTheUrl()
    {
        var handler = new RecordingHandler();
        var client = new GeminiClient(new HttpClient(handler));

        await client.ListModelsAsync(Key, CancellationToken.None);
        await client.CountTokensAsync(Key, "gemini-3.8-flash", "text", CancellationToken.None);
        await client.GenerateContentAsync(Key, "gemini-3.8-flash", new GeminiGenerateContentRequest(
            Contents: new List<GeminiContent> { new(Role: "user", Parts: new List<GeminiPart> { new("hi") }) },
            CachedContent: null, SystemInstruction: null, GenerationConfig: null, SafetySettings: null), CancellationToken.None);
        await client.CreateCachedContentAsync(Key, "gemini-3.8-flash", "system", TimeSpan.FromMinutes(5), CancellationToken.None);
        await client.DeleteCachedContentAsync(Key, "cachedContents/abc", CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.DoesNotContain("key=", r.Url, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Key, r.Url, StringComparison.Ordinal);
            Assert.Equal(Key, r.HeaderKey);
        });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Url, string? HeaderKey)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add((url, request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null));
            var body = url.Contains(":countTokens") ? "{\"totalTokens\":3}"
                : url.Contains(":generateContent") ? "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"안녕\"}]},\"finishReason\":\"STOP\"}]}"
                : url.EndsWith("cachedContents") ? "{\"name\":\"cachedContents/abc\"}"
                : url.Contains("/models?") ? "{\"models\":[]}"
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
