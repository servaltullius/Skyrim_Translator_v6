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
    // (a better description than the patch's) stays, a row they never had is added. A row new to the seed (v8's Sea
    // Squall) has no previous text: it is added, and a row of that source the user wrote themselves stays.
    [Fact]
    public async Task SeedChanges_ReplaceOnlyRowsStillHoldingThePreviousSeedText()
    {
        var path = Path.Combine(_root, "bundled-skyrim-tes-franchise-tm-v2-changes.tsv");
        await File.WriteAllTextAsync(path,
            "Source\tTarget\tPrevious\n"
            + "Ice Storm\t얼음 폭풍\t얼음의 형상\n"
            + "A blast of cold.\t차가운 돌풍을 뿜어냅니다.\t냉기를 뿜습니다.\n"
            + "Ice Spike\t얼음 가시\t얼음 창\n"
            + "Sea Squall\t바다 소나기 호\t\n"
            + "Northern Maiden\t북방의 처녀호\t\n");
        var global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        try
        {
            var tm = new FranchiseTranslationMemoryService(global);
            await tm.BulkUpsertAsync("english", "korean",
                new List<(string, string)>
                {
                    ("Ice Storm", "얼음의 형상"), ("A blast of cold.", "체력에 피해를 주는 냉기를 내뿜습니다."), ("Northern Maiden", "노던 메이든 호"),
                },
                CancellationToken.None);

            Assert.Equal(3, await tm.ImportFromTsvAsync("english", "korean", path, CancellationToken.None));

            var entries = await tm.GetEntriesAsync("english", "korean", CancellationToken.None);
            Assert.Contains(entries, e => e.SourceText == "Ice Storm" && e.DestText == "얼음 폭풍");
            Assert.Contains(entries, e => e.SourceText == "A blast of cold." && e.DestText == "체력에 피해를 주는 냉기를 내뿜습니다.");
            Assert.Contains(entries, e => e.SourceText == "Ice Spike" && e.DestText == "얼음 가시");
            Assert.Contains(entries, e => e.SourceText == "Sea Squall" && e.DestText == "바다 소나기 호");
            Assert.Contains(entries, e => e.SourceText == "Northern Maiden" && e.DestText == "노던 메이든 호");
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
