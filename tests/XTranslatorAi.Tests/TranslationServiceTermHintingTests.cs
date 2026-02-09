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
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class TranslationServiceTermHintingTests
{
    private const string ModelName = "gemini-3.0-flash-preview";

    [Fact]
    public async Task TranslateIdsAsync_IncludesTermHintMarkersInPrompt_ForKoreanForceTokens()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await db.BulkInsertGlossaryAsync(
                new[]
                {
                    (
                        Category: (string?)null,
                        SourceTerm: "bandit",
                        TargetTerm: "산적",
                        Enabled: true,
                        Priority: 10,
                        MatchMode: (int)GlossaryMatchMode.WordBoundary,
                        ForceMode: (int)GlossaryForceMode.ForceToken,
                        Note: (string?)null
                    ),
                },
                CancellationToken.None
            );

            await InsertPendingStringsAsync(
                db,
                new[]
                {
                    (OrderIndex: 1, SourceText: "bandit"),
                    (OrderIndex: 2, SourceText: "bandit"),
                }
            );
            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            Assert.Equal(2, ids.Count);

            var handler = new RecordingPromptGeminiHandler();
            var httpClient = new HttpClient(handler);
            var service = new TranslationService(db, new GeminiClient(httpClient));

            await service.TranslateIdsAsync(CreateTranslateIdsRequest(ids));

            Assert.NotNull(handler.LastUserPrompt);
            Assert.Contains("⟦XT_TERM=산적⟧", handler.LastUserPrompt!, StringComparison.Ordinal);
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
            BatchSize: 10,
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
            CancellationToken: CancellationToken.None,
            EnablePromptCache: false,
            EnableTemplateFixer: false
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

    private static async Task InsertPendingStringsAsync(ProjectDb db, IReadOnlyList<(int OrderIndex, string SourceText)> rows)
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

    private sealed class RecordingPromptGeminiHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string? LastUserPrompt { get; private set; }

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
            LastUserPrompt = userPrompt;

            if (IsBatchPrompt(userPrompt))
            {
                var ids = ExtractIdsFromBatchPrompt(userPrompt);
                var translations = ids
                    .Select(id => new Dictionary<string, object?> { ["id"] = id, ["text"] = "__XT_TERM_0000__" })
                    .ToList();
                var candidateText = JsonSerializer.Serialize(new { translations }, JsonOptions);
                return OkJson(BuildGenerateContentResponse(candidateText));
            }

            return OkJson(BuildGenerateContentResponse("__XT_TERM_0000__ __XT_PH_9999__"));
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
            return parts[0].GetProperty("text").GetString() ?? "";
        }

        private static List<long> ExtractIdsFromBatchPrompt(string userPrompt)
        {
            const string marker = "Input JSON:";
            var idx = userPrompt.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(idx >= 0);
            var json = userPrompt[(idx + marker.Length)..].Trim();
            using var doc = JsonDocument.Parse(json);
            var ids = new List<long>();
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                ids.Add(item.GetProperty("id").GetInt64());
            }
            return ids;
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

        private static HttpResponseMessage OkJson(object payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
            };
        }
    }
}

