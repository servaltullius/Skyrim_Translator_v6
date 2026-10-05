using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Diagnostics;
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

    // With failover off, a spent daily quota made each remaining batch wait through its 429 retries and end as
    // Error, for hours on a large project. Three failed batches in a row now stop the run; the batch that tripped
    // the limit and the rows never sent stay Pending so the run can be resumed later.
    [Theory]
    [InlineData(1, 5, 2, 3)]
    [InlineData(2, 8, 4, 4)]
    public async Task TranslateIdsAsync_WhenFailoverDisabled_StopsAfterThreeRateLimitedBatches(int batchSize, int rows, int errors, int pending)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var ids = await InsertPendingStringsAsync(db, rows);

            var handler = new Always429Handler();
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            var ex = await Assert.ThrowsAsync<TranslationRateLimitAbortException>(
                () => service.TranslateIdsAsync(CreateRequest(ids, enableApiKeyFailover: false) with { BatchSize = batchSize }));

            Assert.Equal(3, handler.Calls);
            var statuses = await db.GetStringStatusesByIdsAsync(ids, CancellationToken.None);
            Assert.Equal(errors, statuses.Values.Count(status => status == StringEntryStatus.Error));
            Assert.Equal(pending, statuses.Values.Count(status => status == StringEntryStatus.Pending));
            var error = UserFacingErrorClassifier.Classify(ex);
            Assert.Equal("E202", error.Code);
            Assert.Contains("멈췄습니다", error.Message);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    // A per-minute limit that lets calls through between failures is not a spent quota.
    [Fact]
    public async Task TranslateIdsAsync_WhenFailoverDisabled_ASuccessfulCallResetsTheRateLimitStreak()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var ids = await InsertPendingStringsAsync(db, 5);

            var handler = new ScriptedHandler(true, true, false, true, true);
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            await service.TranslateIdsAsync(CreateRequest(ids, enableApiKeyFailover: false));

            Assert.Equal(5, handler.Calls);
            var statuses = await db.GetStringStatusesByIdsAsync(ids, CancellationToken.None);
            Assert.Equal(4, statuses.Values.Count(status => status == StringEntryStatus.Error));
            Assert.Equal(1, statuses.Values.Count(status => status == StringEntryStatus.Done));
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    // Gemini answers a wrong or expired key with 400 API_KEY_INVALID, not 401. Treated as a plain batch
    // failure, every remaining row of a large project became Error and the next saved key was never tried.
    [Fact]
    public async Task TranslateIdsAsync_InvalidKeyAnsweredWith400_StopsAndRestoresPending()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var id = await InsertPendingStringAsync(db);

            var handler = new FixedErrorHandler(HttpStatusCode.BadRequest, "Bad Request",
                "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\","
                + "\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}");
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            await Assert.ThrowsAnyAsync<Exception>(() => service.TranslateIdsAsync(CreateRequest(new[] { id }, enableApiKeyFailover: false)));

            var state = await db.GetStringTranslationStateAsync(id, CancellationToken.None);
            Assert.Equal(StringEntryStatus.Pending, state.Status);
            Assert.Equal(1, handler.Calls);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    // Any other 400 (here a bad parameter) is a row failure, not a key problem.
    [Fact]
    public async Task TranslateIdsAsync_Other400_MarksTheRowError()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var id = await InsertPendingStringAsync(db);

            var handler = new FixedErrorHandler(HttpStatusCode.BadRequest, "Bad Request",
                "{\"error\":{\"code\":400,\"message\":\"Invalid value at 'generation_config.temperature'\",\"status\":\"INVALID_ARGUMENT\"}}");
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            await service.TranslateIdsAsync(CreateRequest(new[] { id }, enableApiKeyFailover: true));

            var state = await db.GetStringTranslationStateAsync(id, CancellationToken.None);
            Assert.Equal(StringEntryStatus.Error, state.Status);
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

    private static async Task<IReadOnlyList<long>> InsertPendingStringsAsync(ProjectDb db, int count)
    {
        await db.BulkInsertStringsAsync(
            Enumerable.Range(1, count).Select(i => (
                OrderIndex: i,
                ListAttr: (string?)null,
                PartialAttr: (string?)null,
                AttributesJson: (string?)null,
                Edid: (string?)$"Test{i:00}",
                Rec: (string?)"MGEF:FULL",
                SourceText: $"Hello {i}",
                DestText: "",
                Status: StringEntryStatus.Pending,
                RawStringXml: "<r/>"
            )).ToArray(),
            CancellationToken.None
        );

        return await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
    }

    /// <summary>Answers each generateContent call with 429 (true) or with the source text echoed back (false).</summary>
    private sealed class ScriptedHandler(params bool[] rateLimited) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.IndexOf(":generateContent", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }

            var call = Calls++;
            if (call < rateLimited.Length && rateLimited[call])
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    ReasonPhrase = "Too Many Requests",
                    Content = new StringContent("{\"error\":{\"code\":429,\"message\":\"quota exceeded\",\"status\":\"RESOURCE_EXHAUSTED\"}}"),
                };
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var prompt = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
            var start = prompt.IndexOf("<<<TEXT", StringComparison.Ordinal) + "<<<TEXT".Length;
            var text = prompt[start..prompt.LastIndexOf("TEXT>>>", StringComparison.Ordinal)].Trim();
            var response = System.Text.Json.JsonSerializer.Serialize(new
            {
                candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text } } } } },
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) };
        }
    }

    private sealed class FixedErrorHandler(HttpStatusCode status, string reason, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.IndexOf(":generateContent", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
            }

            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { ReasonPhrase = reason, Content = new StringContent(body) });
        }
    }

    // A lost connection or a timeout used to make every remaining row Error (connection refused: 40 rows in 16 s;
    // 15-minute timeouts: 236 calls for 40 rows, as each timed-out batch was split again and again). Three failures
    // in a row now stop the run like a spent quota does, and leave the rest Pending.
    // With key failover on, the first connection failure was rethrown as a failover error, but the runner no longer
    // switches keys for E210/E211, so a moment of Wi-Fi trouble ended the whole run. Both settings now behave alike.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TranslateIdsAsync_StopsAfterThreeConnectionFailures_WithOrWithoutFailover(bool timeout, bool failover)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var ids = await InsertPendingStringsAsync(db, 12);

            var handler = new NoAnswerHandler(timeout);
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            var ex = await Assert.ThrowsAsync<TranslationRateLimitAbortException>(
                () => service.TranslateIdsAsync(CreateRequest(ids, enableApiKeyFailover: failover) with { BatchSize = 4 }));

            Assert.Equal(timeout ? 6 : 3, handler.Calls);
            var statuses = await db.GetStringStatusesByIdsAsync(ids, CancellationToken.None);
            Assert.True(statuses.Values.Count(status => status == StringEntryStatus.Pending) >= 4);
            var error = UserFacingErrorClassifier.Classify(ex);
            Assert.Equal("E211", error.Code);
            Assert.Contains("멈췄습니다", error.Message);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    // "The model is overloaded." (503) on every call marked all 12 rows Error within seconds; an overload lasting minutes
    // turned a large project into thousands of Error rows. It stops like a lost connection, rows left Pending, and no
    // other key is tried for it.
    [Fact]
    public async Task TranslateIdsAsync_StopsAfterThreeServerOverloads()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var ids = await InsertPendingStringsAsync(db, 12);

            var handler = new OverloadedHandler();
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            var ex = await Assert.ThrowsAsync<TranslationRateLimitAbortException>(
                () => service.TranslateIdsAsync(CreateRequest(ids, enableApiKeyFailover: false) with { BatchSize = 4 }));

            var statuses = await db.GetStringStatusesByIdsAsync(ids, CancellationToken.None);
            Assert.True(statuses.Values.Count(status => status == StringEntryStatus.Pending) >= 4);
            var error = UserFacingErrorClassifier.Classify(ex);
            Assert.Equal("E204", error.Code);
            Assert.Contains("멈췄습니다", error.Message);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    private sealed class OverloadedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var status = url.IndexOf(":generateContent", StringComparison.OrdinalIgnoreCase) < 0 ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"error\":{\"code\":503,\"message\":\"The model is overloaded.\",\"status\":\"UNAVAILABLE\"}}"),
            });
        }
    }

    private sealed class NoAnswerHandler(bool timeout) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.IndexOf(":generateContent", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
            }

            Calls++;
            if (timeout)
            {
                // What HttpClient throws when its Timeout elapses.
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.",
                    new TimeoutException("The operation was canceled."));
            }

            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
        }
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
