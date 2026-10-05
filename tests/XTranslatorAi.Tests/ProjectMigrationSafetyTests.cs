using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public sealed class ProjectMigrationSafetyTests
{
    private static readonly XTranslatorXmlInfo Info = new("Same.esp", "english", "korean", "2", false, "<?xml version=\"1.0\"?>");

    [Fact]
    public void SameAddonAcrossFranchises_HasDistinctPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "xt-path-test-" + Guid.NewGuid().ToString("N"));
        var paths = Enum.GetValues<BethesdaFranchise>()
            .Select(franchise => ProjectPaths.GetProjectDbPath(franchise, "Same.esp", "english", "korean", root)).ToList();
        Assert.Equal(3, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task LegacyMigration_PreservesSourceAndIncludesCommittedWalData()
    {
        var root = Path.Combine(Path.GetTempPath(), "xt-migration-test-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "old.sqlite");
        var destination = Path.Combine(root, "fallout", "new.sqlite");
        try
        {
            await using var old = await CreateLegacyAsync(legacy, BethesdaFranchise.Fallout);
            await old.BulkUpsertTranslationMemoryAsync("english", "korean", new[] { ("source", "번역") }, CancellationToken.None);
            Assert.True(await ProjectWorkspaceService.TryMigrateLegacyProjectDbAsync(legacy, destination,
                BethesdaFranchise.Fallout, Info, Path.Combine(root, "input.xml"), CancellationToken.None));
            Assert.True(File.Exists(legacy));
            await using var migrated = await ProjectDb.OpenOrCreateAsync(destination, CancellationToken.None);
            Assert.Equal("번역", (await migrated.GetTranslationMemoryAsync("english", "korean", CancellationToken.None))["source"]);
            Assert.Equal(BethesdaFranchise.Fallout, (await migrated.TryGetProjectAsync(CancellationToken.None))!.Franchise);
            Assert.False(await ProjectWorkspaceService.TryMigrateLegacyProjectDbAsync(legacy, destination,
                BethesdaFranchise.Fallout, Info, Path.Combine(root, "input.xml"), CancellationToken.None));
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(legacy);
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(destination);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LegacyMigration_RejectsAnotherFranchise()
    {
        var root = Path.Combine(Path.GetTempPath(), "xt-migration-test-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "old.sqlite");
        var destination = Path.Combine(root, "starfield", "new.sqlite");
        try
        {
            await using var old = await CreateLegacyAsync(legacy, BethesdaFranchise.ElderScrolls);
            Assert.False(await ProjectWorkspaceService.TryMigrateLegacyProjectDbAsync(legacy, destination,
                BethesdaFranchise.Starfield, Info, Path.Combine(root, "input.xml"), CancellationToken.None));
            Assert.False(File.Exists(destination));
            Assert.Equal(BethesdaFranchise.ElderScrolls, (await old.TryGetProjectAsync(CancellationToken.None))!.Franchise);
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(legacy);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LegacyWithoutFranchise_RequiresExactInputPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "xt-migration-test-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "old.sqlite");
        var destination = Path.Combine(root, "new.sqlite");
        try
        {
            await using var old = await CreateLegacyAsync(legacy, null);
            Assert.False(await ProjectWorkspaceService.TryMigrateLegacyProjectDbAsync(legacy, destination,
                BethesdaFranchise.ElderScrolls, Info, Path.Combine(root, "different.xml"), CancellationToken.None));
            Assert.True(await ProjectWorkspaceService.TryMigrateLegacyProjectDbAsync(legacy, destination,
                BethesdaFranchise.ElderScrolls, Info, Path.Combine(root, "input.xml"), CancellationToken.None));
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(legacy);
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(destination);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // A damaged old project DB threw out of the migration, so the XML could not be opened at all.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DamagedLegacyDb_IsSkipped(bool sqliteWithoutProject)
    {
        var root = Path.Combine(Path.GetTempPath(), "xt-migration-test-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "old.sqlite");
        var destination = Path.Combine(root, "new.sqlite");
        try
        {
            Directory.CreateDirectory(root);
            if (sqliteWithoutProject)
            {
                await using var empty = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={legacy};Pooling=False");
                await empty.OpenAsync();
                await using var create = empty.CreateCommand();
                create.CommandText = "CREATE TABLE Other (Id INTEGER);";
                await create.ExecuteNonQueryAsync();
            }
            else
            {
                await File.WriteAllTextAsync(legacy, "this is not a database, just text that happens to be long enough");
            }

            Assert.False(await ProjectWorkspaceService.TryMigrateLegacyProjectDbAsync(legacy, destination,
                BethesdaFranchise.ElderScrolls, Info, Path.Combine(root, "input.xml"), CancellationToken.None));
            Assert.False(File.Exists(destination));
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(legacy);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<ProjectDb> CreateLegacyAsync(string path, BethesdaFranchise? franchise)
    {
        var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
        await db.UpsertProjectAsync(new ProjectInfo(1, Path.Combine(Path.GetDirectoryName(path)!, "input.xml"),
            Info.AddonName, franchise, Info.SourceLang, Info.DestLang, Info.Version, false, Info.PrologLine,
            "model", "base", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
        return db;
    }
}
