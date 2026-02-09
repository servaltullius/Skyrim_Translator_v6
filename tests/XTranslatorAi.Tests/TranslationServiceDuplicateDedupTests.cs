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
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class TranslationServiceDuplicateDedupTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_DeduplicatesIdenticalInputs_AndProducesIdenticalOutputs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            const string source = "A skill beyond the reach of most.";
            await InsertPendingStringsAsync(db,
                (OrderIndex: 1, Rec: "PERK:DESC", SourceText: source),
                (OrderIndex: 2, Rec: "PERK:DESC", SourceText: source)
            );

            var ids = await GetPendingIdsAsync(db, expectedCount: 2);

            var service = CreateService(db, new FakeGeminiHandler());

            var request = CreateTranslateIdsRequest(ids, batchSize: 10);
            await service.TranslateIdsAsync(request);

            var (first, second) = await GetRowsByIdsAsync(db, ids[0], ids[1]);

            Assert.Equal(StringEntryStatus.Done, first.Status);
            Assert.Equal(StringEntryStatus.Done, second.Status);
            Assert.Equal(first.DestText, second.DestText);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_WhenBatchContainsDuplicates_CopiesCanonicalResultToDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            const string source = "A skill beyond the reach of most.";
            const string otherSource = "A different sentence.";
            await InsertPendingStringsAsync(db,
                (OrderIndex: 1, Rec: "PERK:DESC", SourceText: source),
                (OrderIndex: 2, Rec: "PERK:DESC", SourceText: source),
                (OrderIndex: 3, Rec: "SPEL:DESC", SourceText: otherSource)
            );

            var ids = await GetPendingIdsAsync(db, expectedCount: 3);

            var service = CreateService(db, new FakeGeminiHandler());

            var request = CreateTranslateIdsRequest(ids, batchSize: 10);
            await service.TranslateIdsAsync(request);

            var (first, second, third) = await GetRowsByIdsAsync(db, ids[0], ids[1], ids[2]);

            Assert.Equal(StringEntryStatus.Done, first.Status);
            Assert.Equal(StringEntryStatus.Done, second.Status);
            Assert.Equal(StringEntryStatus.Done, third.Status);

            Assert.Equal("KOR_A", first.DestText);
            Assert.Equal(first.DestText, second.DestText);
            Assert.Equal("KOR_B", third.DestText);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_DoesNotDeduplicate_WhenGlossaryTokenReplacementsDiffer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            await InsertForceTokenGlossaryAsync(db, category: "eldenrim", "Ash of War", "전회");
            await InsertForceTokenGlossaryAsync(db, category: "eldenrim", "Strength", "근력");
            await InsertForceTokenGlossaryAsync(db, category: "eldenrim", "Dexterity", "기량");

            const string sourceA = "Ash of War - Strength";
            const string sourceB = "Ash of War - Dexterity";
            await InsertPendingStringsAsync(db,
                (OrderIndex: 1, Rec: "MGEF:FULL", SourceText: sourceA),
                (OrderIndex: 2, Rec: "MGEF:FULL", SourceText: sourceB)
            );

            var ids = await GetPendingIdsAsync(db, expectedCount: 2);

            var service = CreateService(db, new FakeGeminiEchoTokensHandler());

            var request = CreateTranslateIdsRequest(ids, batchSize: 10);
            await service.TranslateIdsAsync(request);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            var first = rows.Single(r => r.SourceText == sourceA);
            var second = rows.Single(r => r.SourceText == sourceB);

            Assert.Equal(StringEntryStatus.Done, first.Status);
            Assert.Equal(StringEntryStatus.Done, second.Status);
            Assert.Equal("전회 - 근력", first.DestText);
            Assert.Equal("전회 - 기량", second.DestText);
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

    private static TranslationService CreateService(ProjectDb db, HttpMessageHandler handler)
    {
        return new TranslationService(db, new GeminiClient(new HttpClient(handler)));
    }

    private static TranslateIdsRequest CreateTranslateIdsRequest(IReadOnlyList<long> ids, int batchSize)
    {
        return new TranslateIdsRequest(
            ApiKey: "DUMMY",
            ModelName: ModelName,
            SourceLang: "english",
            TargetLang: "korean",
            SystemPrompt: "base",
            Ids: ids,
            BatchSize: batchSize,
            MaxChars: 5000,
            MaxConcurrency: 2,
            Temperature: 0.2,
            MaxOutputTokens: 512,
            MaxRetries: 0,
            UseRecStyleHints: false,
            EnableRepairPass: false,
            EnableSessionTermMemory: false,
            OnRowUpdated: null,
            WaitIfPaused: null,
            CancellationToken: CancellationToken.None
        );
    }

    private static async Task<IReadOnlyList<long>> GetPendingIdsAsync(ProjectDb db, int expectedCount)
    {
        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        Assert.Equal(expectedCount, ids.Count);
        return ids;
    }

    private static async Task InsertPendingStringsAsync(
        ProjectDb db,
        params (int OrderIndex, string Rec, string SourceText)[] rows
    )
    {
        await db.BulkInsertStringsAsync(
            rows.Select(
                    r =>
                    (
                        OrderIndex: r.OrderIndex,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)null,
                        Rec: (string?)r.Rec,
                        SourceText: r.SourceText,
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    )
                )
                .ToArray(),
            CancellationToken.None
        );
    }

    private static async Task InsertForceTokenGlossaryAsync(
        ProjectDb db,
        string? category,
        string sourceTerm,
        string targetTerm
    )
    {
        await db.BulkInsertGlossaryAsync(
            new[]
            {
                (
                    Category: category,
                    SourceTerm: sourceTerm,
                    TargetTerm: targetTerm,
                    Enabled: true,
                    Priority: 10,
                    MatchMode: (int)GlossaryMatchMode.WordBoundary,
                    ForceMode: (int)GlossaryForceMode.ForceToken,
                    Note: (string?)null
                ),
            },
            CancellationToken.None
        );
    }

    private static async Task<(StringEntry A, StringEntry B)> GetRowsByIdsAsync(ProjectDb db, long a, long b)
    {
        var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
        return (
            rows.Single(r => r.Id == a),
            rows.Single(r => r.Id == b)
        );
    }

    private static async Task<(StringEntry A, StringEntry B, StringEntry C)> GetRowsByIdsAsync(ProjectDb db, long a, long b, long c)
    {
        var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
        return (
            rows.Single(r => r.Id == a),
            rows.Single(r => r.Id == b),
            rows.Single(r => r.Id == c)
        );
    }

    private sealed class FakeGeminiEchoTokensHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

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
                var userPrompt = ExtractUserPrompt(body);

                var ids = ExtractIdsFromBatchPrompt(userPrompt);
                Assert.True(ids.Count > 0);

                var translations = new List<Dictionary<string, object?>>(capacity: ids.Count);
                foreach (var id in ids)
                {
                    translations.Add(
                        new Dictionary<string, object?>
                        {
                            ["id"] = id,
                            ["text"] = "__XT_TERM_0000__ - __XT_TERM_0001__",
                        }
                    );
                }

                return OkJson(
                    new
                    {
                        candidates = new[]
                        {
                            new
                            {
                                content = new { parts = new[] { new { text = JsonSerializer.Serialize(new { translations }, JsonOptions) } } },
                                finishReason = "STOP",
                            },
                        },
                    }
                );
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            };
        }

        private static string ExtractUserPrompt(string requestJson)
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            var contents = root.GetProperty("contents");
            var first = contents[0];
            var parts = first.GetProperty("parts");
            var text = parts[0].GetProperty("text").GetString();
            return text ?? "";
        }

        private static List<long> ExtractIdsFromBatchPrompt(string userPrompt)
        {
            const string marker = "Input JSON:";
            var idx = userPrompt.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return new List<long>();
            }

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
    }

    private sealed class FakeGeminiHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

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
                var userPrompt = ExtractUserPrompt(body);

                var candidateText = IsBatchPrompt(userPrompt)
                    ? BuildBatchTranslationsText(userPrompt)
                    : "KOR_A";

                return OkJson(
                    new
                    {
                        candidates = new[]
                        {
                            new
                            {
                                content = new { parts = new[] { new { text = candidateText } } },
                                finishReason = "STOP",
                            },
                        },
                    }
                );
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            };
        }

        private static bool IsBatchPrompt(string userPrompt)
            => userPrompt.IndexOf("Input JSON:", StringComparison.Ordinal) >= 0;

        private static string ExtractUserPrompt(string requestJson)
        {
            using var doc = JsonDocument.Parse(requestJson);
            var root = doc.RootElement;
            var contents = root.GetProperty("contents");
            var first = contents[0];
            var parts = first.GetProperty("parts");
            var text = parts[0].GetProperty("text").GetString();
            return text ?? "";
        }

        private static string BuildBatchTranslationsText(string userPrompt)
        {
            var ids = ExtractIdsFromBatchPrompt(userPrompt);
            Assert.True(ids.Count > 0);

            var translations = new List<Dictionary<string, object?>>(capacity: ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                var text = i == 0 ? "KOR_A" : "KOR_B";
                translations.Add(
                    new Dictionary<string, object?>
                    {
                        ["id"] = ids[i],
                        ["text"] = text,
                    }
                );
            }

            return JsonSerializer.Serialize(new { translations }, JsonOptions);
        }

        private static List<long> ExtractIdsFromBatchPrompt(string userPrompt)
        {
            const string marker = "Input JSON:";
            var idx = userPrompt.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
            {
                return new List<long>();
            }

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
