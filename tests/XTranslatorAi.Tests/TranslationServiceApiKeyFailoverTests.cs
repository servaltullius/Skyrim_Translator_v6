using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class TranslationServiceApiKeyFailoverTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_WhenFailoverDisabled_DoesNotThrowAndMarksError_On429()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var id = await InsertPendingStringAsync(db);

            var handler = new Always429Handler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateRequest(new[] { id }, enableApiKeyFailover: false);
            await service.TranslateIdsAsync(request);

            var state = await db.GetStringTranslationStateAsync(id, CancellationToken.None);
            Assert.Equal(StringEntryStatus.Error, state.Status);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_WhenFailoverEnabled_ThrowsAndRestoresPending_On429()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var id = await InsertPendingStringAsync(db);

            var handler = new Always429Handler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateRequest(new[] { id }, enableApiKeyFailover: true);
            await Assert.ThrowsAnyAsync<Exception>(() => service.TranslateIdsAsync(request));

            var state = await db.GetStringTranslationStateAsync(id, CancellationToken.None);
            Assert.Equal(StringEntryStatus.Pending, state.Status);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedBudget_DoesNotResetWhenKeyAndModelChange(bool useTotalCap)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var id = await InsertPendingStringAsync(db);
            var handler = new Always429Handler();
            using var httpClient = new HttpClient(handler);
            var budget = new TranslationGenerationBudget(useTotalCap ? 8 : 0, useTotalCap ? 1 : 100);
            var request = CreateRequest(new[] { id }, true) with { GenerationBudget = budget };
            var first = new TranslationService(db, new GeminiClient(httpClient));
            await Assert.ThrowsAnyAsync<Exception>(() => first.TranslateIdsAsync(request));
            var second = new TranslationService(db, new GeminiClient(httpClient));
            var next = request with { ApiKey = "DUMMY_SECOND", ModelName = "gemini-2.5-flash" };
            if (useTotalCap) await Assert.ThrowsAnyAsync<Exception>(() => second.TranslateIdsAsync(next));
            else await second.TranslateIdsAsync(next);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(1, budget.TotalCalls);
            Assert.Equal(useTotalCap ? StringEntryStatus.Pending : StringEntryStatus.Error,
                (await db.GetStringTranslationStateAsync(id, CancellationToken.None)).Status);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    private static TranslateIdsRequest CreateRequest(IReadOnlyList<long> ids, bool enableApiKeyFailover)
        => new(
            ApiKey: "DUMMY",
            ModelName: ModelName,
            SourceLang: "english",
            TargetLang: "korean",
            SystemPrompt: "base",
            Ids: ids,
            BatchSize: 1,
            MaxChars: 5000,
            MaxConcurrency: 1,
            Temperature: 0.2,
            MaxOutputTokens: 512,
            MaxRetries: 0,
            UseRecStyleHints: false,
            EnableRepairPass: false,
            EnableSessionTermMemory: false,
            OnRowUpdated: null,
            WaitIfPaused: null,
            CancellationToken: CancellationToken.None,
            EnablePromptCache: false,
            EnableApiKeyFailover: enableApiKeyFailover
        );

    private static async Task SeedProjectAsync(ProjectDb db)
    {
        var now = DateTimeOffset.UtcNow;
        await db.UpsertProjectAsync(
            new ProjectInfo(
                Id: 1,
                InputXmlPath: "C:\\dummy.xml",
                AddonName: "Dummy",
                Franchise: null,
                SourceLang: "english",
                DestLang: "korean",
                XmlVersion: "1",
                XmlHasBom: false,
                XmlPrologLine: "<?xml version=\"1.0\"?>",
                ModelName: ModelName,
                BasePromptText: "base",
                CustomPromptText: null,
                UseCustomPrompt: false,
                CreatedAt: now,
                UpdatedAt: now
            ),
            CancellationToken.None
        );
    }

    private static async Task<long> InsertPendingStringAsync(ProjectDb db)
    {
        await db.BulkInsertStringsAsync(
            new[]
            {
                (
                    OrderIndex: 1,
                    ListAttr: (string?)null,
                    PartialAttr: (string?)null,
                    AttributesJson: (string?)null,
                    Edid: (string?)"Test01",
                    Rec: (string?)"MGEF:FULL",
                    SourceText: "Hello",
                    DestText: "",
                    Status: StringEntryStatus.Pending,
                    RawStringXml: "<r/>"
                ),
            },
            CancellationToken.None
        );

        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        Assert.Single(ids);
        return ids[0];
    }

    private sealed class Always429Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.IndexOf(":generateContent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Calls++;
                var resp = new HttpResponseMessage((HttpStatusCode)429)
                {
                    ReasonPhrase = "Too Many Requests",
                    Content = new StringContent(
                        "{\"error\":{\"code\":429,\"message\":\"quota exceeded\",\"status\":\"RESOURCE_EXHAUSTED\"}}"
                    ),
                };
                return Task.FromResult(resp);
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}"),
                }
            );
        }
    }
}
