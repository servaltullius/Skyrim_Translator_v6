using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public class SessionTermAutoGlossaryPersistenceTests
{
    [Fact]
    public async Task FlushSessionTermAutoGlossaryInsertsAsync_PersistsToProjectGlossary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var service = new TranslationService(db, new GeminiClient(new HttpClient()));

            service._ctx = new TranslationRunContext { EnableSessionTermMemory = true };

            var queue = new ConcurrentQueue<(string Source, string Target)>();
            queue.Enqueue(("Ancient Dragons' Lightning Spear", "고룡의 뇌창"));
            service._ctx.PendingSessionAutoGlossaryInserts = queue;

            await service.FlushSessionTermAutoGlossaryInsertsAsync();

            var rows = await db.GetGlossaryAsync(CancellationToken.None);
            Assert.NotEmpty(rows);
            Assert.Contains(rows, r => r.SourceTerm == "Ancient Dragons' Lightning Spear" && r.TargetTerm == "고룡의 뇌창");
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task FlushSessionTermAutoGlossaryInsertsAsync_DoesNotDuplicate_WhenCalledTwice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var service = new TranslationService(db, new GeminiClient(new HttpClient()));

            service._ctx = new TranslationRunContext { EnableSessionTermMemory = true };

            var queue = new ConcurrentQueue<(string Source, string Target)>();
            queue.Enqueue(("Alduin", "알두인"));
            service._ctx.PendingSessionAutoGlossaryInserts = queue;

            await service.FlushSessionTermAutoGlossaryInsertsAsync();

            // Enqueue the same term again
            queue.Enqueue(("Alduin", "알두인"));
            await service.FlushSessionTermAutoGlossaryInsertsAsync();

            var rows = await db.GetGlossaryAsync(CancellationToken.None);
            var matchCount = 0;
            foreach (var r in rows)
            {
                if (r.SourceTerm == "Alduin")
                {
                    matchCount++;
                }
            }

            Assert.Equal(1, matchCount);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task FlushSessionTermAutoGlossaryInsertsAsync_SkipsEmpty_WhenQueueIsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var service = new TranslationService(db, new GeminiClient(new HttpClient()));

            service._ctx = new TranslationRunContext { EnableSessionTermMemory = true };
            // PendingSessionAutoGlossaryInserts is null

            await service.FlushSessionTermAutoGlossaryInsertsAsync();

            var rows = await db.GetGlossaryAsync(CancellationToken.None);
            Assert.Empty(rows);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }
}
