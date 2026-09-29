using System.Net;
using System.Net.Http;
using System.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class GeminiListModelsTests
{
    [Fact]
    public async Task ListModels_ReadsAllPagesAndDeduplicatesNames()
    {
        var handler = new PagesHandler(
            """{"models":[{"name":"models/gemini-3.8-flash"}],"nextPageToken":"a+/=?&"}""",
            """{"models":[{"name":"models/gemini-3.8-flash"},{"name":"models/gemini-3.5-flash-lite"}]}""");
        using var http = new HttpClient(handler);
        var models = await new GeminiClient(http).ListModelsAsync("DUMMY", CancellationToken.None);

        Assert.Equal(2, models.Count);
        Assert.Equal(2, handler.Urls.Count);
        Assert.All(handler.Urls, url => Assert.Contains("pageSize=1000", url));
        Assert.DoesNotContain("pageToken", handler.Urls[0]);
        Assert.Contains("pageToken=a%2B%2F%3D%3F%26", handler.Urls[1]);
    }

    [Fact]
    public async Task ListModels_RepeatedPageTokenStopsWithError()
    {
        var handler = new PagesHandler(
            """{"models":[],"nextPageToken":"repeat"}""",
            """{"models":[],"nextPageToken":"repeat"}""");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<GeminiException>(() => new GeminiClient(http).ListModelsAsync("DUMMY", CancellationToken.None));
        Assert.Equal(2, handler.Urls.Count);
    }

    [Fact]
    public async Task ListModels_FailedLaterPageDoesNotReturnPartialSuccess()
    {
        var handler = new PagesHandler("""{"models":[{"name":"models/gemini-3.8-flash"}],"nextPageToken":"next"}""");
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<GeminiHttpException>(() => new GeminiClient(http).ListModelsAsync("DUMMY", CancellationToken.None));
        Assert.Equal(2, handler.Urls.Count);
    }

    [Fact]
    public async Task ListModels_PreCancelledRequestDoesNotMakeHttpCall()
    {
        var handler = new PagesHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GeminiClient(http).ListModelsAsync("DUMMY", new CancellationToken(true)));
        Assert.Empty(handler.Urls);
    }

    private sealed class PagesHandler(params string[] pages) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Urls.Count;
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(index < pages.Length ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(index < pages.Length ? pages[index] : "unavailable", Encoding.UTF8, "application/json"),
            });
        }
    }
}
