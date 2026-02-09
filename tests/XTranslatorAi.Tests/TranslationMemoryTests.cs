using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class TranslationMemoryTests
{
    private const string ModelName = "gemini-3-flash-preview";

    [Fact]
    public async Task UpdateStringTranslationAsync_Edited_PersistsTranslationMemory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await InsertPendingStringsAsync(db, ("INFO:NAM1", "Ancient Dragons' Lightning Spear"));

            var id = (await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None)).Single();
            await db.UpdateStringTranslationAsync(id, "고룡의 뇌창", StringEntryStatus.Edited, null, CancellationToken.None);
        }
        finally
        {
            await AssertTranslationMemoryRowAsync(path, "english", "korean", "ancient dragons' lightning spear", "고룡의 뇌창");
            TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_UsesTranslationMemory_ForPendingRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await InsertPendingStringsAsync(
                db,
                ("INFO:NAM1", "Ancient Dragons' Lightning Spear"),
                ("INFO:NAM1", "Ancient Dragons' Lightning Spear")
            );

            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            Assert.Equal(2, ids.Count);

            // Create TM via a manual edit on the first row.
            await db.UpdateStringTranslationAsync(ids[0], "고룡의 뇌창", StringEntryStatus.Edited, null, CancellationToken.None);
            await db.UpdateStringStatusAsync(ids[1], StringEntryStatus.Pending, errorMessage: null, CancellationToken.None);

            // Fail-fast HTTP client so we can detect whether LLM translation was attempted.
            var httpClient = new HttpClient(new FailFastHandler());
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateTranslateIdsRequest(ids[1]);
            await service.TranslateIdsAsync(request);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            var translated = rows.Single(r => r.Id == ids[1]);
            Assert.Equal(StringEntryStatus.Done, translated.Status);
            Assert.Equal("고룡의 뇌창", translated.DestText);
        }
        finally
        {
            TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_UsesGlobalTranslationMemory_ForPendingRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await InsertPendingStringsAsync(db, ("INFO:NAM1", "Ancient Dragons' Lightning Spear"));

            var id = (await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None)).Single();

            // Fail-fast HTTP client so we can detect whether LLM translation was attempted.
            var httpClient = new HttpClient(new FailFastHandler());
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateTranslateIdsRequest(id) with
            {
                GlobalTranslationMemory = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ancient dragons' lightning spear"] = "고룡의 뇌창",
                },
            };

            await service.TranslateIdsAsync(request);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            var translated = rows.Single(r => r.Id == id);
            Assert.Equal(StringEntryStatus.Done, translated.Status);
            Assert.Equal("고룡의 뇌창", translated.DestText);
        }
        finally
        {
            TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task TranslateIdsAsync_TranslationMemory_AppliesKoreanPostEdits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            await InsertPendingStringsAsync(db, ("MGEF", "Absorb Magicka"));

            var id = (await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None)).Single();

            // Fail-fast HTTP client so we can detect whether LLM translation was attempted.
            var httpClient = new HttpClient(new FailFastHandler());
            var service = new TranslationService(db, new GeminiClient(httpClient));

            var request = CreateTranslateIdsRequest(id) with
            {
                GlobalTranslationMemory = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["absorb magicka"] = "매지카을 흡수합니다.",
                },
            };

            await service.TranslateIdsAsync(request);

            var rows = await db.GetStringsAsync(limit: 10, offset: 0, CancellationToken.None);
            var translated = rows.Single(r => r.Id == id);
            Assert.Equal(StringEntryStatus.Done, translated.Status);
            Assert.Equal("매지카를 흡수합니다.", translated.DestText);
        }
        finally
        {
            TryDeleteDbFiles(path);
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

    private static async Task InsertPendingStringsAsync(ProjectDb db, params (string Rec, string SourceText)[] rows)
    {
        await db.BulkInsertStringsAsync(
            rows.Select(
                    (r, i) =>
                    (
                        OrderIndex: i + 1,
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
            Temperature: 0.1,
            MaxOutputTokens: 1024,
            MaxRetries: 0,
            UseRecStyleHints: false,
            EnableRepairPass: false,
            EnableSessionTermMemory: false,
            OnRowUpdated: null,
            WaitIfPaused: null,
            CancellationToken: CancellationToken.None
        );
    }

    private static async Task AssertTranslationMemoryRowAsync(
        string dbPath,
        string sourceLangKey,
        string destLangKey,
        string srcKey,
        string dstText
    )
    {
        // Query the raw DB to verify persistence without relying on new APIs.
        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT COUNT(1)
            FROM TranslationMemory
            WHERE SourceLangKey=$SourceLangKey
              AND DestLangKey=$DestLangKey
              AND SrcKey=$SrcKey
              AND DstText=$DstText;
            """;
        cmd.Parameters.AddWithValue("$SourceLangKey", sourceLangKey);
        cmd.Parameters.AddWithValue("$DestLangKey", destLangKey);
        cmd.Parameters.AddWithValue("$SrcKey", srcKey);
        cmd.Parameters.AddWithValue("$DstText", dstText);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(1, count);
    }

    private sealed class FailFastHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("fail-fast"),
            });
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

    private static void TryDeleteDbFiles(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }
}
