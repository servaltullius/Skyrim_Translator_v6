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

public class TranslationServiceBatchGroupingTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_SortsBatchItemsByEdidStem_ForConsistency()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            var rows = new List<(int OrderIndex, string Edid, string SourceText)>
            {
                (1, "WarAshItem01", "Ash of War - Strength"),
                (2, "OtherSkill01", "Another Skill - Alpha"),
                (3, "WarAshItem02", "Ash of War - Dexterity"),
                (4, "OtherSkill02", "Another Skill - Beta"),
                (5, "WarAshItem03", "Ash of War - Arcane"),
                (6, "OtherSkill03", "Another Skill - Gamma"),
                (7, "WarAshItem04", "Ash of War - Fortune"),
                (8, "OtherSkill04", "Another Skill - Delta"),
            };

            await InsertPendingStringsAsync(db, rows);
            var ids = await GetPendingIdsAsync(db, expectedCount: 8);

            var idToStem = await BuildIdToEdidStemMapAsync(db, ids.Count);

            var handler = new RecordingGeminiHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateTranslateIdsRequest(ids, batchSize: 3);
            await service.TranslateIdsAsync(request);

            Assert.True(handler.Batches.Count >= 1);
            AssertBatchesSortedByStem(handler.Batches, idToStem);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static async Task InsertPendingStringsAsync(ProjectDb db, IReadOnlyList<(int OrderIndex, string Edid, string SourceText)> rows)
    {
        await db.BulkInsertStringsAsync(
            rows.Select(
                    r =>
                    (
                        OrderIndex: r.OrderIndex,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)r.Edid,
                        Rec: (string?)"MGEF:FULL",
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

    private static async Task<IReadOnlyList<long>> GetPendingIdsAsync(ProjectDb db, int expectedCount)
    {
        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        Assert.Equal(expectedCount, ids.Count);
        return ids;
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

    private static async Task<Dictionary<long, string>> BuildIdToEdidStemMapAsync(ProjectDb db, int expectedCount)
    {
        var idToStem = new Dictionary<long, string>(capacity: expectedCount);
        var dbRows = await db.GetStringsAsync(limit: 100, offset: 0, CancellationToken.None);
        foreach (var row in dbRows)
        {
            idToStem[row.Id] = StripTrailingDigits(row.Edid ?? "");
        }
        return idToStem;
    }

    private static void AssertBatchesSortedByStem(
        IReadOnlyList<IReadOnlyList<long>> batches,
        IReadOnlyDictionary<long, string> idToStem
    )
    {
        foreach (var batch in batches)
        {
            var stems = batch.Select(id => idToStem[id]).ToList();
            Assert.True(stems.Count > 0);
            AssertSorted(stems);
        }
    }

    private static void AssertSorted(IReadOnlyList<string> values)
    {
        for (var i = 1; i < values.Count; i++)
        {
            Assert.True(
                StringComparer.OrdinalIgnoreCase.Compare(values[i - 1], values[i]) <= 0,
                $"Stem order is not sorted within a batch at {i}: {values[i - 1]} > {values[i]}"
            );
        }
    }

    private sealed class RecordingGeminiHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly object _gate = new();

        public List<IReadOnlyList<long>> Batches { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (IsCreateCachedContent(url))
            {
                return OkJson(new { name = "cachedContents/test" });
            }

            if (IsGenerateContent(url))
            {
                return await HandleGenerateContentAsync(request, cancellationToken);
            }

            if (IsDeleteCachedContent(request.Method, url))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            };
        }

        private static bool IsCreateCachedContent(string url)
            => url.Contains("/cachedContents?", StringComparison.OrdinalIgnoreCase);

        private static bool IsGenerateContent(string url)
            => url.Contains(":generateContent?", StringComparison.OrdinalIgnoreCase);

        private static bool IsDeleteCachedContent(HttpMethod method, string url)
            => method == HttpMethod.Delete && url.Contains("/cachedContents/", StringComparison.OrdinalIgnoreCase);

        private async Task<HttpResponseMessage> HandleGenerateContentAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var userPrompt = ExtractUserPrompt(body);

            return IsBatchPrompt(userPrompt)
                ? HandleBatchGenerateContent(userPrompt)
                : OkJson(BuildGenerateContentResponse("KOR_SINGLE"));
        }

        private HttpResponseMessage HandleBatchGenerateContent(string userPrompt)
        {
            var ids = ExtractIdsFromBatchPrompt(userPrompt);
            lock (_gate)
            {
                Batches.Add(ids);
            }

            var translations = ids.Select(id => new Dictionary<string, object?> { ["id"] = id, ["text"] = $"KOR_{id}" }).ToList();
            var candidateText = JsonSerializer.Serialize(new { translations }, JsonOptions);
            return OkJson(BuildGenerateContentResponse(candidateText));
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

    private static string StripTrailingDigits(string value)
    {
        value = (value ?? "").Trim();
        var end = value.Length;
        while (end > 0 && char.IsDigit(value[end - 1]))
        {
            end--;
        }

        if (end <= 0)
        {
            return "";
        }

        value = value[..end].TrimEnd('_', '-', ' ');
        return value;
    }
}
