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

public class TranslationServiceTranslationMemoryValidationTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_SkipsTranslationMemory_WhenPlaceholdersMismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            const string sourceText = "Absorb <mag> points of Magicka.";
            await db.BulkInsertStringsAsync(
                new[]
                {
                    (
                        OrderIndex: 1,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)null,
                        Rec: (string?)"MGEF",
                        SourceText: sourceText,
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                },
                CancellationToken.None
            );

            var id = (await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None)).Single();

            // TM entry intentionally drops <mag>, which should fail placeholder validation and be skipped.
            var tm = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TranslationMemoryKey.NormalizeSource(sourceText)] = "매지카를 흡수합니다.",
            };

            var handler = new CountingGeminiHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateTranslateIdsRequest(id) with
            {
                GlobalTranslationMemory = tm,
                KeepSkyrimTagsRaw = true,
                EnableTemplateFixer = false,
            };

            await service.TranslateIdsAsync(request);

            Assert.True(handler.GenerateCalls >= 1);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            var translated = rows.Single(r => r.Id == id);
            Assert.Equal(StringEntryStatus.Done, translated.Status);
            Assert.Contains("<mag>", translated.DestText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LLM_OK_", translated.DestText, StringComparison.Ordinal);

            var notes = await db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None);
            Assert.True(notes.TryGetValue(id, out var note));
            Assert.Contains("TM 폴백", note, StringComparison.Ordinal);
            Assert.Contains("Skyrim placeholder mismatch", note, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_RecordsTmHit_WhenTranslationMemoryApplied()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            const string sourceText = "Absorb <mag> points of Magicka.";
            await db.BulkInsertStringsAsync(
                new[]
                {
                    (
                        OrderIndex: 1,
                        ListAttr: (string?)null,
                        PartialAttr: (string?)null,
                        AttributesJson: (string?)null,
                        Edid: (string?)null,
                        Rec: (string?)"MGEF",
                        SourceText: sourceText,
                        DestText: "",
                        Status: StringEntryStatus.Pending,
                        RawStringXml: "<r/>"
                    ),
                },
                CancellationToken.None
            );

            var id = (await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None)).Single();

            var tm = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TranslationMemoryKey.NormalizeSource(sourceText)] = "<mag> TM_OK_1",
            };

            var handler = new CountingGeminiHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateTranslateIdsRequest(id) with
            {
                GlobalTranslationMemory = tm,
                KeepSkyrimTagsRaw = true,
                EnableTemplateFixer = false,
            };

            await service.TranslateIdsAsync(request);

            Assert.Equal(0, handler.GenerateCalls);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            var translated = rows.Single(r => r.Id == id);
            Assert.Equal(StringEntryStatus.Done, translated.Status);
            Assert.Contains("<mag>", translated.DestText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("TM_OK_", translated.DestText, StringComparison.Ordinal);

            var notes = await db.GetStringNotesByKindAsync("tm_hit", CancellationToken.None);
            Assert.True(notes.ContainsKey(id));
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

    private static TranslateIdsRequest CreateTranslateIdsRequest(long id)
    {
        return new TranslateIdsRequest(
            ApiKey: "DUMMY",
            ModelName: ModelName,
            SourceLang: "english",
            TargetLang: "korean",
            SystemPrompt: "base",
            Ids: new[] { id },
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
            CancellationToken: CancellationToken.None
        );
    }

    private sealed class CountingGeminiHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private int _generateCalls;
        public int GenerateCalls => _generateCalls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";

            if (IsCreateCachedContent(url))
            {
                return OkJson(new { name = "cachedContents/test" });
            }

            if (IsGenerateContent(url))
            {
                Interlocked.Increment(ref _generateCalls);
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

        private async Task<HttpResponseMessage> HandleGenerateContentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var userPrompt = ExtractUserPrompt(body);

            if (!IsBatchPrompt(userPrompt))
            {
                return OkJson(BuildGenerateContentResponse("<mag> LLM_OK_SINGLE"));
            }

            var ids = ExtractIdsFromBatchPrompt(userPrompt);
            var translations = ids.Select(id => new Dictionary<string, object?> { ["id"] = id, ["text"] = $"<mag> LLM_OK_{id}" }).ToList();

            var candidateText = JsonSerializer.Serialize(new { translations }, JsonOptions);
            return OkJson(BuildGenerateContentResponse(candidateText));
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
