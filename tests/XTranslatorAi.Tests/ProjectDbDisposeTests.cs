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
    public async Task ReleasePool_LetsThatDbBeDeleted_AndLeavesAnotherOpenDbWorking()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-dispose-{Guid.NewGuid():N}.sqlite");
        var otherPath = Path.Combine(Path.GetTempPath(), $"xt-dispose-{Guid.NewGuid():N}.sqlite");
        var other = await ProjectDb.OpenOrCreateAsync(otherPath, CancellationToken.None);
        try
        {
            await (await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None)).DisposeAsync();

            ProjectDb.ReleasePool(path);
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
            {
                File.Delete(file);
            }

            Assert.False(File.Exists(path));
            Assert.Equal(0, await other.GetStringCountAsync(CancellationToken.None));
        }
        finally
        {
            await other.DisposeAsync();
            ProjectDb.ReleasePool(otherPath);
            foreach (var file in new[] { otherPath, otherPath + "-wal", otherPath + "-shm" })
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// SqliteConnection.ClearAllPools disposes the pooled connections of every database, and a DB another thread was
    /// opening at that moment failed with "Cannot access a disposed object" (a test failing now and then, a different
    /// one each time, while another test cleaned up; in the app, deleting a failed first open's empty project).
    /// </summary>
    [Fact]
    public void NoCodeClearsEverySqlitePool()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var offenders = new[] { "src", "tests", "tools" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !file.EndsWith(nameof(ProjectDbDisposeTests) + ".cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("ClearAllPools(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.Empty(offenders);
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
