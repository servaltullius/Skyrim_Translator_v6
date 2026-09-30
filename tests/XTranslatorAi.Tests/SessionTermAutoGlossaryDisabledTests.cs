using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public class SessionTermAutoGlossaryPersistenceTests
{
    [Fact]
    public async Task ConflictedQueuedSuggestion_IsNotPersisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var memory = new TranslationService.SessionTermMemory(200);
            memory.TryLearn("Weapon Art", "전투 기술", allowForce: false);
            memory.TryLearn("Weapon Art", "무기 기술", allowForce: false);
            var queue = new ConcurrentQueue<(string Source, string Target)>();
            queue.Enqueue(("Weapon Art", "전투 기술"));
            var service = new TranslationService(db, new GeminiClient(new HttpClient()));
            service._ctx = new TranslationRunContext
            {
                EnableSessionTermMemory = true, SessionTermMemory = memory, PendingSessionAutoGlossaryInserts = queue,
            };
            await service.FlushSessionTermAutoGlossaryInsertsAsync();
            Assert.Empty(await db.GetGlossaryAsync(CancellationToken.None));
        }
        finally { TestDbHelper.TryDeleteDbFiles(path); }
    }

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
            var row = Assert.Single(rows);
            Assert.Equal("Ancient Dragons' Lightning Spear", row.SourceTerm);
            Assert.Equal("고룡의 뇌창", row.TargetTerm);
            Assert.False(row.Enabled);
            Assert.Equal(GlossaryForceMode.PromptOnly, row.ForceMode);
            Assert.Equal(GlossaryMatchMode.WordBoundary, row.MatchMode);
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
    public async Task FlushSessionTermAutoGlossaryInsertsAsync_PreservesExistingReviewedTerm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await db.TryInsertGlossaryIfMissingAsync(new GlossaryUpsertRequest(
                "Manual", "Alduin", "수동 확정", true, 100, GlossaryMatchMode.Substring,
                GlossaryForceMode.ForceToken, "reviewed"), CancellationToken.None);
            var before = Assert.Single(await db.GetGlossaryAsync(CancellationToken.None));
            var queue = new ConcurrentQueue<(string Source, string Target)>();
            queue.Enqueue(("Alduin", "자동 후보"));
            var service = new TranslationService(db, new GeminiClient(new HttpClient()));
            service._ctx = new TranslationRunContext
            {
                EnableSessionTermMemory = true, PendingSessionAutoGlossaryInserts = queue,
            };
            await service.FlushSessionTermAutoGlossaryInsertsAsync();
            Assert.Equal(before, Assert.Single(await db.GetGlossaryAsync(CancellationToken.None)));
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
