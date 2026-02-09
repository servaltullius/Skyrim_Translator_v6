using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class SessionTermAutoGlossaryDisabledTests
{
    [Fact]
    public async Task FlushSessionTermAutoGlossaryInsertsAsync_DoesNotPersistToProjectGlossary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var service = new TranslationService(db, new GeminiClient(new HttpClient()));

            SetPrivateField(service, "_enableSessionTermMemory", true);

            var queue = new ConcurrentQueue<(string Source, string Target)>();
            queue.Enqueue(("Ancient Dragons' Lightning Spear", "고룡의 뇌창"));
            SetPrivateField(service, "_pendingSessionAutoGlossaryInserts", queue);

            await InvokePrivateAsync(service, "FlushSessionTermAutoGlossaryInsertsAsync");

            var rows = await db.GetGlossaryAsync(CancellationToken.None);
            Assert.Empty(rows);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        var field = target
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static async Task InvokePrivateAsync(object target, string methodName)
    {
        var method = target
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(target, Array.Empty<object?>());
        Assert.NotNull(task);
        await task!;
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

