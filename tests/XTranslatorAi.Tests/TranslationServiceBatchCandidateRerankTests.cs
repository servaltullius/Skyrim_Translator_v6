using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

public sealed class TranslationServiceBatchCandidateRerankTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_RiskyBatch_UsesMultipleCandidatesAndSelectsBetterOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            const string sourceText = "Protect Whiterun from bandit attacks.";
            const string sourceText2 = "Protect Solitude from bandit attacks.";
            await db.BulkInsertStringsAsync(
                new[]
                {
                    (
                        OrderIndex: 1,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)"WhiterunGuard01",
                        Rec: (string?)"MGEF:FULL",
                        SourceText: sourceText,
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                    (
                        OrderIndex: 2,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)"WhiterunGuard02",
                        Rec: (string?)"MGEF:FULL",
                        SourceText: sourceText2,
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                },
                CancellationToken.None
            );

            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            Assert.Equal(2, ids.Count);

            var handler = new MultiCandidateHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            await service.TranslateIdsAsync(CreateTranslateIdsRequest(ids));

            Assert.Equal(3, handler.LastBatchCandidateCount);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row => Assert.Equal("화이트런을 산적의 공격으로부터 보호하라.", row.DestText));
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static TranslateIdsRequest CreateTranslateIdsRequest(IReadOnlyList<long> ids)
    {
        return new TranslateIdsRequest(
            ApiKey: "DUMMY",
            ModelName: ModelName,
            SourceLang: "english",
            TargetLang: "korean",
            SystemPrompt: "base",
            Ids: ids,
            BatchSize: 4,
            MaxChars: 5000,
            MaxConcurrency: 1,
            Temperature: 0.1,
            MaxOutputTokens: 512,
            MaxRetries: 0,
            UseRecStyleHints: false,
            EnableRepairPass: false,
            EnableSessionTermMemory: false,
            OnRowUpdated: null,
            WaitIfPaused: null,
            CancellationToken: CancellationToken.None,
            EnablePromptCache: false
        );
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

    private sealed class MultiCandidateHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public int? LastBatchCandidateCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (!url.Contains(":generateContent?", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
                };
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var userPrompt = ExtractUserPrompt(body);
            if (!IsBatchPrompt(userPrompt))
            {
                return OkJson(BuildGenerateContentResponse("__XT_PH_9999__"));
            }

            LastBatchCandidateCount = ExtractCandidateCount(body);
            var ids = ExtractIdsFromBatchPrompt(userPrompt);

            var badCandidateText = BuildTranslationsText(ids, "Protect Whiterun from bandit attacks.");
            var goodCandidateText = BuildTranslationsText(ids, "화이트런을 산적의 공격으로부터 보호하라.");

            var payload = new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new { parts = new[] { new { text = badCandidateText } } },
                        finishReason = "STOP",
                    },
                    new
                    {
                        content = new { parts = new[] { new { text = goodCandidateText } } },
                        finishReason = "STOP",
                    },
                },
            };

            return OkJson(payload);
        }

        private static int? ExtractCandidateCount(string requestJson)
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("generationConfig", out var generationConfig))
            {
                return null;
            }

            if (!generationConfig.TryGetProperty("candidateCount", out var candidateCount))
            {
                return null;
            }

            return candidateCount.GetInt32();
        }

        private static bool IsBatchPrompt(string userPrompt)
            => userPrompt.IndexOf("Input JSON:", StringComparison.Ordinal) >= 0;

        private static string BuildTranslationsText(IReadOnlyList<long> ids, string text)
        {
            var translations = ids
                .Select(id => new Dictionary<string, object?> { ["id"] = id, ["text"] = text })
                .ToList();

            return JsonSerializer.Serialize(new { translations }, JsonOptions);
        }

        private static string ExtractUserPrompt(string requestJson)
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            var contents = root.GetProperty("contents");
            var first = contents[0];
            var parts = first.GetProperty("parts");
            return parts[0].GetProperty("text").GetString() ?? "";
        }

        private static IReadOnlyList<long> ExtractIdsFromBatchPrompt(string userPrompt)
        {
            const string marker = "Input JSON:";
            var idx = userPrompt.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx >= 0);

            var json = userPrompt[(idx + marker.Length)..].Trim();
            using var doc = JsonDocument.Parse(json);

            var list = new List<long>();
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                list.Add(item.GetProperty("id").GetInt64());
            }

            return list;
        }

        private static HttpResponseMessage OkJson<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj, JsonOptions);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }

        private static object BuildGenerateContentResponse(string text)
        {
            return new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new { parts = new[] { new { text } } },
                        finishReason = "STOP",
                    },
                },
            };
        }
    }
}
