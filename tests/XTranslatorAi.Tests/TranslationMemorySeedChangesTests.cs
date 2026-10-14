using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public sealed class TranslationMemorySeedChangesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-seed-changes-" + Guid.NewGuid().ToString("N"));

    public TranslationMemorySeedChangesTests() => Directory.CreateDirectory(_root);

    // The v2 seed changes a bundled row only where the user still has the v1 text: a row they edited in the global memory
    // (a better description than the patch's) stays, a row they never had is added.
    [Fact]
    public async Task SeedChanges_ReplaceOnlyRowsStillHoldingThePreviousSeedText()
    {
        var path = Path.Combine(_root, "bundled-skyrim-tes-franchise-tm-v2-changes.tsv");
        await File.WriteAllTextAsync(path,
            "Source\tTarget\tPrevious\n"
            + "Ice Storm\t얼음 폭풍\t얼음의 형상\n"
            + "A blast of cold.\t차가운 돌풍을 뿜어냅니다.\t냉기를 뿜습니다.\n"
            + "Ice Spike\t얼음 가시\t얼음 창\n");
        var global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        try
        {
            var tm = new FranchiseTranslationMemoryService(global);
            await tm.BulkUpsertAsync("english", "korean",
                new List<(string, string)> { ("Ice Storm", "얼음의 형상"), ("A blast of cold.", "체력에 피해를 주는 냉기를 내뿜습니다.") },
                CancellationToken.None);

            Assert.Equal(2, await tm.ImportFromTsvAsync("english", "korean", path, CancellationToken.None));

            var entries = await tm.GetEntriesAsync("english", "korean", CancellationToken.None);
            Assert.Contains(entries, e => e.SourceText == "Ice Storm" && e.DestText == "얼음 폭풍");
            Assert.Contains(entries, e => e.SourceText == "A blast of cold." && e.DestText == "체력에 피해를 주는 냉기를 내뿜습니다.");
            Assert.Contains(entries, e => e.SourceText == "Ice Spike" && e.DestText == "얼음 가시");
        }
        finally
        {
            await global.DisposeAsync();
            foreach (var db in Directory.GetFiles(_root, "*.sqlite", SearchOption.AllDirectories))
            {
                TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
