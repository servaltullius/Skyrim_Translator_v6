using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// A disposed ProjectDb returned its connection to the SQLite pool, which kept the file open, so Compare's
/// temporary DB was never deleted (thousands of files piled up in %TEMP%\TulliusTranslator\compare).
/// </summary>
public sealed class ProjectDbDisposeTests
{
    [Fact]
    public async Task DisposedDb_CanBeDeletedRightAway()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-dispose-{Guid.NewGuid():N}.sqlite");
        var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
        await db.BulkInsertStringsAsync(new[] { (0, (string?)null, (string?)null, (string?)null,
            (string?)null, (string?)"WEAP:FULL", "Iron Sword", "", StringEntryStatus.Pending, "<String />") }, CancellationToken.None);

        await db.DisposeAsync();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DisposingOneDb_LeavesAnotherOpenOnTheSameFileWorking()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-dispose-{Guid.NewGuid():N}.sqlite");
        var first = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
        var second = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
        try
        {
            await first.DisposeAsync();
            await second.BulkInsertStringsAsync(new[] { (0, (string?)null, (string?)null, (string?)null,
                (string?)null, (string?)"WEAP:FULL", "Iron Sword", "", StringEntryStatus.Pending, "<String />") }, CancellationToken.None);

            Assert.Equal(1, await second.GetStringCountAsync(CancellationToken.None));
        }
        finally
        {
            await second.DisposeAsync();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                File.Delete(file);
            }
        }
    }
}
