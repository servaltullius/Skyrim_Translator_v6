using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class TranslationServicePromptCacheRecoveryTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_WhenCachedContentIsInvalid_RetriesWithoutCache()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            await db.BulkInsertStringsAsync(
                new[]
                {
                    (
                        OrderIndex: 1,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)"BookTest01",
                        Rec: (string?)"BOOK:FULL",
                        SourceText: "Hello world.",
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                },
                CancellationToken.None
            );

            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            Assert.Single(ids);

            var handler = new CachedContent403ThenOkHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = new TranslateIdsRequest(
                ApiKey: "DUMMY",
                ModelName: ModelName,
                SourceLang: "english",
                TargetLang: "korean",
                SystemPrompt: "base",
                Ids: ids,
                BatchSize: 1,
                MaxChars: 5000,
                MaxConcurrency: 1,
                Temperature: 0.0,
                MaxOutputTokens: 512,
                MaxRetries: 0,
                UseRecStyleHints: false,
                EnableRepairPass: false,
                EnableSessionTermMemory: false,
                OnRowUpdated: null,
                WaitIfPaused: null,
                CancellationToken: CancellationToken.None
            );

            await service.TranslateIdsAsync(request);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            Assert.Single(rows);
            Assert.Equal(StringEntryStatus.Done, rows[0].Status);
            Assert.Equal("KOR_OK", rows[0].DestText);

            Assert.Equal(1, handler.GenerateWithCacheCount);
            Assert.Equal(1, handler.GenerateWithoutCacheCount);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_WhenCachedContentPermissionDenied_DisablesCacheForRemainingRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            await db.BulkInsertStringsAsync(
                new[]
                {
                    (
                        OrderIndex: 1,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)"BookTest01",
                        Rec: (string?)"BOOK:FULL",
                        SourceText: "Hello world.",
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                    (
                        OrderIndex: 2,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)"BookTest02",
                        Rec: (string?)"BOOK:FULL",
                        SourceText: "Second line.",
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                },
                CancellationToken.None
            );

            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            Assert.Equal(2, ids.Count);

            var handler = new CachedContent403ThenOkHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = new TranslateIdsRequest(
                ApiKey: "DUMMY",
                ModelName: ModelName,
                SourceLang: "english",
                TargetLang: "korean",
                SystemPrompt: "base",
                Ids: ids,
                BatchSize: 1,
                MaxChars: 5000,
                MaxConcurrency: 1,
                Temperature: 0.0,
                MaxOutputTokens: 512,
                MaxRetries: 0,
                UseRecStyleHints: false,
                EnableRepairPass: false,
                EnableSessionTermMemory: false,
                OnRowUpdated: null,
                WaitIfPaused: null,
                CancellationToken: CancellationToken.None
            );

            await service.TranslateIdsAsync(request);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal(StringEntryStatus.Done, r.Status));

            // First row: generate with cache fails (403), then we retry without cache.
            // Second row: cache should be disabled and go straight to no-cache.
            Assert.Equal(1, handler.GenerateWithCacheCount);
            Assert.Equal(2, handler.GenerateWithoutCacheCount);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

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

    private sealed class CachedContent403ThenOkHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public int GenerateWithCacheCount { get; private set; }
        public int GenerateWithoutCacheCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (url.Contains("/cachedContents?", StringComparison.OrdinalIgnoreCase))
            {
                return OkJson(new { name = "cachedContents/test" });
            }

            if (url.Contains(":generateContent?", StringComparison.OrdinalIgnoreCase))
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.IndexOf("\"cachedContent\"", StringComparison.Ordinal) >= 0)
                {
                    GenerateWithCacheCount++;
                    return new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent(
                            "{\"error\":{\"code\":403,\"message\":\"CachedContent not found (or permission denied)\",\"status\":\"PERMISSION_DENIED\"}}",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    };
                }

                GenerateWithoutCacheCount++;
                return OkJson(
                    new
                    {
                        candidates = new[]
                        {
                            new
                            {
                                content = new { parts = new[] { new { text = "KOR_OK __XT_PH_9999__" } } },
                                finishReason = "STOP",
                            },
                        },
                    }
                );
            }

            if (request.Method == HttpMethod.Delete && url.Contains("/cachedContents/", StringComparison.OrdinalIgnoreCase))
            {
                return OkJson(new { ok = true });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            };
        }

        private static HttpResponseMessage OkJson<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj, JsonOptions);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
