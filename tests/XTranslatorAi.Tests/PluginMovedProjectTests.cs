using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Projects are keyed on the plugin's full path. Moving the Elden Rim folder into a subfolder opened new, empty
/// projects, and the mod was translated again without its reviewed rows, project glossary and context.
/// </summary>
public sealed class PluginMovedProjectTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-moved-plugin", Guid.NewGuid().ToString("N"));
    private ProjectWorkspaceService _workspace = null!;
    private GlobalProjectDbService _global = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        _workspace = new ProjectWorkspaceService(_global, Path.Combine(_root, "projects"));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _global.DisposeAsync();
        // Only these tests' DBs: clearing every pool broke DBs other tests were opening at the same time.
        foreach (var db in Directory.EnumerateFiles(_root, "*.sqlite", SearchOption.AllDirectories))
        {
            XTranslatorAi.Core.Data.ProjectDb.ReleasePool(db);
        }
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<ProjectWorkspaceService.LoadFromPluginResult> OpenAsync(string path, string targetEncoding = "utf-8")
        => await _workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(path, new PluginReadOptions(),
            "korean", targetEncoding, "gemini-3.8-flash", "", false), CancellationToken.None);

    private async Task<string> TranslateOnlyRowAsync(string pluginPath)
    {
        var opened = await OpenAsync(pluginPath);
        await using (opened.Db)
        {
            var row = Assert.Single(opened.Entries);
            await opened.Db.UpdateStringTranslationAsync(row.Id, "철검", StringEntryStatus.Edited, null, CancellationToken.None);
            await opened.Db.UpsertGlossaryAsync(new(null, "Weapon Art", "전기", true, 10, XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary,
                XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, "Elden Rim 기준 용어"), CancellationToken.None);
        }

        return pluginPath;
    }

    private string WritePlugin(string folder, string itemName = "Iron Sword")
    {
        var path = Path.Combine(_root, folder, "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PluginProjectIntegrationTests.CreateMinimalPlugin(itemName));
        return path;
    }

    [Fact]
    public async Task MovedPlugin_ContinuesItsProject_WithTranslationsAndGlossary()
    {
        var before = await TranslateOnlyRowAsync(WritePlugin(@"mods\0-Elden Rim"));
        var after = Path.Combine(_root, @"mods\엘든림\0-Elden Rim", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(after)!);
        File.Move(before, after);

        var reopened = await OpenAsync(after);
        await using (reopened.Db)
        {
            Assert.Equal(before, reopened.MovedFromPath);
            var row = Assert.Single(reopened.Entries);
            Assert.Equal(("철검", StringEntryStatus.Edited), (row.DestText, row.Status));
            Assert.Contains(await reopened.Db.GetGlossaryAsync(CancellationToken.None), g => g.SourceTerm == "Weapon Art");
        }

        // The earlier project is retired to .moved, leaving only the new .sqlite project DB.
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "projects"), "Test.*.sqlite", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "projects"), "Test.*.sqlite.moved", SearchOption.AllDirectories));
    }

    // The output encoding is part of the project key; following E457's advice (choose another output encoding and
    // reopen) opened an empty project.
    [Fact]
    public async Task ReopeningWithAnotherOutputEncoding_ContinuesTheProject()
    {
        var plugin = await TranslateOnlyRowAsync(WritePlugin("a"));

        var reopened = await OpenAsync(plugin, targetEncoding: "windows-1252");
        await using (reopened.Db)
        {
            Assert.Null(reopened.MovedFromPath);
            Assert.Equal("utf-8", reopened.ContinuedFromTargetEncoding);
            Assert.Equal(("철검", StringEntryStatus.Edited), (Assert.Single(reopened.Entries).DestText, Assert.Single(reopened.Entries).Status));
        }
    }

    // The Strings folder and the metadata encoding are part of the project key as well: setting a Strings folder for
    // Skyrim.esm, or following E452's advice to change the metadata encoding, opened every other plugin empty.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReopeningWithOtherReadSettings_ContinuesTheProject(bool stringsFolder, bool metadataEncoding)
    {
        var plugin = await TranslateOnlyRowAsync(WritePlugin("a"));
        var strings = Path.Combine(_root, "strings");
        Directory.CreateDirectory(strings);
        var options = new PluginReadOptions() with
        {
            StringsDirectory = stringsFolder ? strings : null,
            MetadataEncoding = metadataEncoding ? "ks_c_5601-1987" : new PluginReadOptions().MetadataEncoding,
        };

        var reopened = await _workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(plugin, options,
            "korean", "utf-8", "gemini-3.8-flash", "", false), CancellationToken.None);
        await using (reopened.Db)
        {
            Assert.Null(reopened.MovedFromPath);
            Assert.True(reopened.ContinuedAfterReadSettingsChange);
            Assert.Equal(("철검", StringEntryStatus.Edited), (Assert.Single(reopened.Entries).DestText, Assert.Single(reopened.Entries).Status));
        }
    }

    // A ";" in a plugin's file name went into an interpolated connection string: the next new plugin failed to open,
    // and a moved "Mod;Fix.esp" could never be continued.
    [Fact]
    public async Task APluginNamedWithASemicolon_DoesNotBreakOpeningPlugins()
    {
        var semicolon = Path.Combine(_root, "a", "Mod;Fix.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(semicolon)!);
        File.WriteAllBytes(semicolon, PluginProjectIntegrationTests.CreateMinimalPlugin());
        await TranslateOnlyRowAsync(semicolon);

        var moved = Path.Combine(_root, "b", "Mod;Fix.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(semicolon, moved);
        var reopened = await OpenAsync(moved);
        await using (reopened.Db)
        {
            Assert.Equal("철검", Assert.Single(reopened.Entries).DestText);
        }

        var other = await OpenAsync(WritePlugin("c"));
        await using (other.Db)
        {
            Assert.Single(other.Entries);
        }
    }

    // A canceled or failed first open left an empty DB at the new path, which every later open took as the project:
    // the moved plugin was never continued again.
    [Fact]
    public async Task FailedFirstOpenOfAMovedPlugin_LeavesNoProject_AndTheNextOpenContinuesIt()
    {
        var before = await TranslateOnlyRowAsync(WritePlugin("a"));
        var after = Path.Combine(_root, "b", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(after)!);
        File.Move(before, after);

        var projects = Path.Combine(_root, "projects");
        var earlier = Assert.Single(Directory.EnumerateFiles(projects, "Test.*.sqlite", SearchOption.AllDirectories));
        _workspace.BeforePluginImportForTests = _ => throw new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OpenAsync(after));
        _workspace.BeforePluginImportForTests = null;
        Assert.All(Directory.EnumerateFiles(projects, "Test.*", SearchOption.AllDirectories), f => Assert.StartsWith(earlier, f));

        var reopened = await OpenAsync(after);
        await using (reopened.Db)
        {
            Assert.Equal(before, reopened.MovedFromPath);
            Assert.Equal("철검", Assert.Single(reopened.Entries).DestText);
        }
    }

    [Fact]
    public async Task CopiedPlugin_WithTheSameContent_ContinuesTheProject()
    {
        var original = await TranslateOnlyRowAsync(WritePlugin("a"));
        var copy = WritePlugin("b");

        var opened = await OpenAsync(copy);
        await using (opened.Db)
        {
            Assert.Equal(original, opened.MovedFromPath);
            Assert.Equal("철검", Assert.Single(opened.Entries).DestText);
        }
    }

    [Fact]
    public async Task DifferentPlugin_WithTheSameNameStillInPlace_GetsItsOwnProject()
    {
        await TranslateOnlyRowAsync(WritePlugin("a"));
        var other = WritePlugin("b", itemName: "Steel Sword");

        var opened = await OpenAsync(other);
        await using (opened.Db)
        {
            Assert.Null(opened.MovedFromPath);
            Assert.Equal(StringEntryStatus.Pending, Assert.Single(opened.Entries).Status);
        }
    }

    // R1: Round-trip move (A -> B -> A) in GUI open preserves B's edits and glossary, and reports MovedFromPath = B.
    [Fact]
    public async Task R1_RoundTripMove_GuiOpen_ContinuesLatestWorkFromB()
    {
        var pathA = await TranslateOnlyRowAsync(WritePlugin("A"));
        var pathB = Path.Combine(_root, "B", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Move(pathA, pathB);

        var openedB = await OpenAsync(pathB);
        await using (openedB.Db)
        {
            Assert.Equal(pathA, openedB.MovedFromPath);
            var rowB = Assert.Single(openedB.Entries);
            Assert.Equal("철검", rowB.DestText);
            await openedB.Db.UpdateStringTranslationAsync(rowB.Id, "강철검", StringEntryStatus.Edited, null, CancellationToken.None);
            await openedB.Db.UpsertGlossaryAsync(new(null, "Ash of War", "전회", true, 10, XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary,
                XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, "B에서 추가"), CancellationToken.None);
        }

        File.Move(pathB, pathA);

        var reopenedA = await OpenAsync(pathA);
        await using (reopenedA.Db)
        {
            Assert.Equal(pathB, reopenedA.MovedFromPath);
            var rowA = Assert.Single(reopenedA.Entries);
            Assert.Equal(("강철검", StringEntryStatus.Edited), (rowA.DestText, rowA.Status));
            var glossary = await reopenedA.Db.GetGlossaryAsync(CancellationToken.None);
            Assert.Contains(glossary, g => g.SourceTerm == "Weapon Art" && g.TargetTerm == "전기");
            Assert.Contains(glossary, g => g.SourceTerm == "Ash of War" && g.TargetTerm == "전회");
        }
    }

    private async Task EditOnlyRowAsync(ProjectWorkspaceService.LoadFromPluginResult opened, string text)
    {
        await using (opened.Db)
        {
            var row = Assert.Single(opened.Entries);
            await opened.Db.UpdateStringTranslationAsync(row.Id, text, StringEntryStatus.Edited, null, CancellationToken.None);
        }
    }

    // The path had a project under another output encoding too (E457's advice: choose another and reopen). Back from
    // B, that one went first as "the same plugin under other settings", and the plugin opened with text older than what
    // was done at B, with no word of it: once the path's own project has gone with the file, the newest of the two is
    // the one to continue.
    [Fact]
    public async Task RoundTrip_ContinuesTheMovedProject_NotAnOlderOneOfOtherSettingsAtThatPath()
    {
        var pathA = WritePlugin("A");
        await EditOnlyRowAsync(await OpenAsync(pathA, "windows-1252"), "old, another output encoding");
        await EditOnlyRowAsync(await OpenAsync(pathA), "mid, at A");
        var pathB = Path.Combine(_root, "B", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Move(pathA, pathB);
        await EditOnlyRowAsync(await OpenAsync(pathB), "new, at B");
        File.Move(pathB, pathA);

        var back = await OpenAsync(pathA);
        await using (back.Db)
        {
            Assert.Equal("new, at B", Assert.Single(back.Entries).DestText);
            Assert.Equal(pathB, back.MovedFromPath);
            Assert.Null(back.ContinuedFromTargetEncoding);
        }
    }

    // The path had a project under a sibling source encoding too. With the path's own project gone with the file, that
    // sibling was "the project that is here" and opened in its place, so nothing was continued.
    [Fact]
    public async Task RoundTrip_ContinuesTheMovedProject_NotAnOlderOneOfASiblingSourceEncodingAtThatPath()
    {
        async Task<ProjectWorkspaceService.LoadFromPluginResult> OpenReadAsAsync(string path, string sourceEncoding)
            => await _workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(path,
                new PluginReadOptions() with { SourceEncoding = sourceEncoding }, "korean", "utf-8", "gemini-3.8-flash", "", false),
                CancellationToken.None);

        var pathA = WritePlugin("A");
        await EditOnlyRowAsync(await OpenAsync(pathA), "main, at A");
        string siblingDb;
        var sibling = await OpenReadAsAsync(pathA, "windows-1252");
        siblingDb = sibling.Db.DbPath;
        await EditOnlyRowAsync(sibling, "side, read as windows-1252");
        string mainDb;
        var main = await OpenAsync(pathA);
        mainDb = main.Db.DbPath;
        await EditOnlyRowAsync(main, "main again, at A");
        Assert.NotEqual(siblingDb, mainDb);

        var pathB = Path.Combine(_root, "B", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Move(pathA, pathB);
        await EditOnlyRowAsync(await OpenAsync(pathB), "new, at B");
        File.Move(pathB, pathA);

        var back = await OpenAsync(pathA);
        await using (back.Db)
        {
            Assert.Equal(mainDb, back.Db.DbPath);
            Assert.Equal("new, at B", Assert.Single(back.Entries).DestText);
            Assert.Equal(pathB, back.MovedFromPath);
        }

        // And it stays so: the path's own project is there again, first.
        var again = await OpenAsync(pathA);
        await using (again.Db)
        {
            Assert.Equal(mainDb, again.Db.DbPath);
            Assert.Equal("new, at B", Assert.Single(again.Entries).DestText);
            Assert.Null(again.LaterWorkElsewherePath);
        }
    }

    // The other way round: the project in use at the path is the one of a sibling source encoding (made before the
    // setting was changed), and nothing of this path went anywhere. A newer project of a deleted copy is then only
    // told of: it opened in this one's place, and what was done here after the copy was not seen again.
    [Fact]
    public async Task ProjectOfASiblingSourceEncoding_InUseAtThePath_IsNotSwappedForANewerOneElsewhere()
    {
        async Task<ProjectWorkspaceService.LoadFromPluginResult> OpenReadAsAsync(string path, string sourceEncoding)
            => await _workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(path,
                new PluginReadOptions() with { SourceEncoding = sourceEncoding }, "korean", "utf-8", "gemini-3.8-flash", "", false),
                CancellationToken.None);

        var pathA = WritePlugin("A");
        string dbA;
        var made = await OpenReadAsAsync(pathA, "windows-1252");
        dbA = made.Db.DbPath;
        await EditOnlyRowAsync(made, "A, made reading windows-1252");
        var sameProject = await OpenAsync(pathA);
        Assert.Equal(dbA, sameProject.Db.DbPath);
        await sameProject.Db.DisposeAsync();

        var pathCopy = WritePlugin("copy");
        string dbCopy;
        var copy = await OpenAsync(pathCopy);
        dbCopy = copy.Db.DbPath;
        await copy.Db.DisposeAsync();
        await EditOnlyRowAsync(await OpenAsync(pathA), "A, after the copy");
        await EditOnlyRowAsync(await OpenAsync(pathCopy), "the copy, later");
        File.Delete(pathCopy);

        var opened = await OpenAsync(pathA);
        await using (opened.Db)
        {
            Assert.Equal(dbA, opened.Db.DbPath);
            Assert.Equal("A, after the copy", Assert.Single(opened.Entries).DestText);
            Assert.Null(opened.MovedFromPath);
            Assert.Equal(pathCopy, opened.LaterWorkElsewherePath);
        }

        Assert.True(File.Exists(dbCopy));
    }

    // R3: Multi-hop move (A -> B -> C -> A -> B) carries the latest work forward on every hop.
    [Fact]
    public async Task R3_MultiHopMove_PreservesLatestWorkEveryStep()
    {
        var pathA = WritePlugin("A");
        var pathB = Path.Combine(_root, "B", "Test.esp");
        var pathC = Path.Combine(_root, "C", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        Directory.CreateDirectory(Path.GetDirectoryName(pathC)!);

        async Task OpenExpectAndEditAsync(string path, string? expectedMovedFrom, string? expectedText, string nextText)
        {
            var opened = await OpenAsync(path);
            await using (opened.Db)
            {
                Assert.Equal(expectedMovedFrom, opened.MovedFromPath);
                var row = Assert.Single(opened.Entries);
                if (expectedText != null) Assert.Equal(expectedText, row.DestText);
                await opened.Db.UpdateStringTranslationAsync(row.Id, nextText, StringEntryStatus.Edited, null, CancellationToken.None);
            }
        }

        await OpenExpectAndEditAsync(pathA, expectedMovedFrom: null, expectedText: null, nextText: "1차");

        File.Move(pathA, pathB);
        await OpenExpectAndEditAsync(pathB, expectedMovedFrom: pathA, expectedText: "1차", nextText: "2차");

        File.Move(pathB, pathC);
        await OpenExpectAndEditAsync(pathC, expectedMovedFrom: pathB, expectedText: "2차", nextText: "3차");

        File.Move(pathC, pathA);
        await OpenExpectAndEditAsync(pathA, expectedMovedFrom: pathC, expectedText: "3차", nextText: "4차");

        File.Move(pathA, pathB);
        var finalB = await OpenAsync(pathB);
        await using (finalB.Db)
        {
            Assert.Equal(pathA, finalB.MovedFromPath);
            Assert.Equal("4차", Assert.Single(finalB.Entries).DestText);
        }
    }

    // R4: After a move, only the new project DB has .sqlite; the old DB is archived as .sqlite.moved with no -wal/-shm
    // sidecars, holds pre-move translations when opened in SQLite, and is ignored by mod-family glossary inheritance.
    [Fact]
    public async Task R4_MovedPlugin_ArchivesOldDbWithoutSidecars_AndExcludedFromModFamilyGlossary()
    {
        var pathA = Path.Combine(_root, @"mods\Elden-Base", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathA)!);
        File.WriteAllBytes(pathA, PluginProjectIntegrationTests.CreateMinimalPlugin("Iron Sword"));

        string oldDbPath;
        var openedA = await OpenAsync(pathA);
        await using (openedA.Db)
        {
            oldDbPath = openedA.Db.DbPath;
            var row = Assert.Single(openedA.Entries);
            await openedA.Db.UpdateStringTranslationAsync(row.Id, "철검", StringEntryStatus.Edited, null, CancellationToken.None);
            await openedA.Db.UpsertGlossaryAsync(new(null, "Weapon Art", "전기", true, 10, XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary,
                XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, null), CancellationToken.None);
            await openedA.Db.UpsertGlossaryAsync(new(null, "ArchivedOnlyTerm", "보관전용", true, 10, XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary,
                XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, null), CancellationToken.None);
        }

        var pathB = Path.Combine(_root, @"mods\Elden-Moved", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Move(pathA, pathB);

        string newDbPath;
        var openedB = await OpenAsync(pathB);
        await using (openedB.Db)
        {
            newDbPath = openedB.Db.DbPath;
            var glossaryB = await openedB.Db.GetGlossaryAsync(CancellationToken.None);
            var archivedOnly = Assert.Single(glossaryB, g => g.SourceTerm == "ArchivedOnlyTerm");
            await openedB.Db.DeleteGlossaryEntryAsync(archivedOnly.Id, CancellationToken.None);
        }

        var projectsDir = Path.Combine(_root, "projects");
        var sqliteFiles = Directory.EnumerateFiles(projectsDir, "Test.*.sqlite", SearchOption.AllDirectories).ToList();
        Assert.Equal(new[] { newDbPath }, sqliteFiles);

        var archivedPath = oldDbPath + ".moved";
        Assert.False(File.Exists(oldDbPath));
        Assert.True(File.Exists(archivedPath));
        Assert.False(File.Exists(oldDbPath + "-wal"));
        Assert.False(File.Exists(oldDbPath + "-shm"));
        Assert.False(File.Exists(archivedPath + "-wal"));
        Assert.False(File.Exists(archivedPath + "-shm"));

        // Archived file opens cleanly in SQLite and holds the pre-move translation.
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = archivedPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
               }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT DestText FROM StringEntry;";
            Assert.Equal("철검", (string?)cmd.ExecuteScalar());
        }

        // Opening another plugin of the same mod family inherits B's active glossary, not terms that exist only in .moved.
        var addonPath = Path.Combine(_root, @"mods\Elden-Addon", "Addon.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(addonPath)!);
        File.WriteAllBytes(addonPath, PluginProjectIntegrationTests.CreateMinimalPlugin("Steel Dagger"));

        var openedAddon = await OpenAsync(addonPath);
        await using (openedAddon.Db)
        {
            var inherited = await openedAddon.Db.GetGlossaryAsync(CancellationToken.None);
            Assert.Contains(inherited, g => g.SourceTerm == "Weapon Art" && g.TargetTerm == "전기");
            Assert.DoesNotContain(inherited, g => g.SourceTerm == "ArchivedOnlyTerm");
        }
    }

    // R5: Opening a copy while the original remains in place leaves the original DB untouched and creates no .moved file.
    [Fact]
    public async Task R5_CopiedPlugin_WithOriginalInPlace_LeavesOriginalDbAndCreatesNoMovedFile()
    {
        var original = await TranslateOnlyRowAsync(WritePlugin("a"));
        var projectsDir = Path.Combine(_root, "projects");
        var originalDb = Assert.Single(Directory.EnumerateFiles(projectsDir, "Test.*.sqlite", SearchOption.AllDirectories));
        var copy = WritePlugin("b");

        var opened = await OpenAsync(copy);
        await using (opened.Db)
        {
            Assert.Equal(original, opened.MovedFromPath);
            Assert.Null(opened.EarlierProjectLeftAt);
            Assert.Equal("철검", Assert.Single(opened.Entries).DestText);
        }

        Assert.True(File.Exists(originalDb));
        Assert.False(File.Exists(originalDb + ".moved"));
        Assert.Equal(2, Directory.EnumerateFiles(projectsDir, "Test.*.sqlite", SearchOption.AllDirectories).Count());
    }

    // R6: Continuing from a missing plugin with different content (SHA mismatch) copies without retiring the old DB.
    [Fact]
    public async Task R6_MovedPluginWithDifferentContent_ContinuesWithoutRetiringOldDb()
    {
        var original = await TranslateOnlyRowAsync(WritePlugin("a", itemName: "Iron Sword"));
        var projectsDir = Path.Combine(_root, "projects");
        var originalDb = Assert.Single(Directory.EnumerateFiles(projectsDir, "Test.*.sqlite", SearchOption.AllDirectories));
        File.Delete(original);

        var different = WritePlugin("b", itemName: "Steel Sword");
        var opened = await OpenAsync(different);
        await using (opened.Db)
        {
            Assert.Equal(original, opened.MovedFromPath);
            Assert.Null(opened.EarlierProjectLeftAt);
            Assert.Contains(await opened.Db.GetGlossaryAsync(CancellationToken.None), g => g.SourceTerm == "Weapon Art");
        }

        Assert.True(File.Exists(originalDb));
        Assert.False(File.Exists(originalDb + ".moved"));
        Assert.Equal(2, Directory.EnumerateFiles(projectsDir, "Test.*.sqlite", SearchOption.AllDirectories).Count());
    }

    // R7: When another connection holds a read transaction on the old DB, opening at the new path still succeeds,
    // leaves the old DB in place, reports EarlierProjectLeftAt + note, and retires once the connection closes.
    [Fact]
    public async Task R7_LockedOldDb_StillOpensNewProject_ReportsNote_AndRetiresAfterUnlock()
    {
        var pathA = await TranslateOnlyRowAsync(WritePlugin("a"));
        var projectsDir = Path.Combine(_root, "projects");
        var oldDbPath = Assert.Single(Directory.EnumerateFiles(projectsDir, "Test.*.sqlite", SearchOption.AllDirectories));
        var pathB = Path.Combine(_root, "b", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Move(pathA, pathB);

        var lockConnString = new SqliteConnectionStringBuilder
        {
            DataSource = oldDbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using (var lockConn = new SqliteConnection(lockConnString))
        {
            lockConn.Open();
            using var tx = lockConn.BeginTransaction();
            using (var cmd = lockConn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT 1 FROM Project LIMIT 1;";
                cmd.ExecuteScalar();
            }

            var openedB = await OpenAsync(pathB);
            await using (openedB.Db)
            {
                Assert.Equal(pathA, openedB.MovedFromPath);
                Assert.Equal("철검", Assert.Single(openedB.Entries).DestText);
                Assert.Equal(oldDbPath, openedB.EarlierProjectLeftAt);
                var notes = PluginOpenNotes.Describe(openedB);
                Assert.Contains(notes, n => n.Text == "옮기기 전 위치의 프로젝트 파일을 다른 프로그램이 쓰고 있어 치우지 못했습니다. 그 위치로 플러그인을 되돌리면 옛 번역이 열릴 수 있습니다.");
            }

            Assert.True(File.Exists(oldDbPath));
            Assert.False(File.Exists(oldDbPath + ".moved"));
        }

        Assert.True(ProjectWorkspaceService.TryRetireMovedProjectDb(oldDbPath));
        Assert.False(File.Exists(oldDbPath));
        Assert.True(File.Exists(oldDbPath + ".moved"));
        Assert.False(File.Exists(oldDbPath + "-wal"));
        Assert.False(File.Exists(oldDbPath + "-shm"));
    }

    // R8: Truncated .partial and stray -wal/-shm files at the new DB destination do not stop continuation.
    [Fact]
    public async Task R8_TruncatedPartialAndStrayWalAtDestination_StillContinuesMovedProject()
    {
        var pathA = await TranslateOnlyRowAsync(WritePlugin("a"));
        var pathB = Path.Combine(_root, "b", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Move(pathA, pathB);

        var destDbPath = ProjectPaths.GetPluginProjectDbPath(pathB, new PluginReadOptions(), "korean", "utf-8",
            Path.Combine(_root, "projects"));
        Directory.CreateDirectory(Path.GetDirectoryName(destDbPath)!);
        await File.WriteAllBytesAsync(destDbPath + ".partial", new byte[] { 1, 2, 3, 4, 5 });
        await File.WriteAllBytesAsync(destDbPath + "-wal", new byte[] { 9, 8, 7, 6, 5 });
        await File.WriteAllBytesAsync(destDbPath + "-shm", new byte[] { 4, 3, 2, 1 });

        var openedB = await OpenAsync(pathB);
        await using (openedB.Db)
        {
            Assert.Equal(pathA, openedB.MovedFromPath);
            Assert.Equal("철검", Assert.Single(openedB.Entries).DestText);
        }

        Assert.False(File.Exists(destDbPath + ".partial"));
    }

    // "Later work elsewhere" is said of this plugin's own project left where the file once was: not of a copy whose file
    // is still in its place, nor of another file of the name whose project happens to be newer.
    [Fact]
    public async Task LaterWorkElsewhere_IsNotSaidOfACopyStillInPlace_NorOfAnotherFileOfTheName()
    {
        static void SetTimes(string dbPath, DateTime utc)
        {
            ProjectDb.ReleasePool(dbPath);
            if (File.Exists(dbPath)) File.SetLastWriteTimeUtc(dbPath, utc);
            if (File.Exists(dbPath + "-wal")) File.SetLastWriteTimeUtc(dbPath + "-wal", utc);
        }

        var baseTime = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var pathA = WritePlugin("a");
        string dbA;
        var openedA = await OpenAsync(pathA);
        dbA = openedA.Db.DbPath;
        await EditOnlyRowAsync(openedA, "A의 번역");

        // A copy in another folder, its file still there, worked on later.
        var pathCopy = WritePlugin("copy");
        string dbCopy;
        var openedCopy = await OpenAsync(pathCopy);
        dbCopy = openedCopy.Db.DbPath;
        await EditOnlyRowAsync(openedCopy, "복사본의 번역");
        SetTimes(dbA, baseTime);
        SetTimes(dbCopy, baseTime.AddMinutes(5));
        var withCopy = await OpenAsync(pathA);
        await using (withCopy.Db)
        {
            Assert.Null(withCopy.LaterWorkElsewherePath);
        }

        // Another file of the name, its file gone, worked on later.
        File.Delete(pathCopy);
        SetTimes(dbCopy, baseTime.AddMinutes(-5));
        var pathOther = WritePlugin("other", itemName: "Steel Sword");
        string dbOther;
        var openedOther = await OpenAsync(pathOther);
        dbOther = openedOther.Db.DbPath;
        await EditOnlyRowAsync(openedOther, "다른 모드의 번역");
        File.Delete(pathOther);
        SetTimes(dbA, baseTime);
        SetTimes(dbOther, baseTime.AddMinutes(5));
        var withOther = await OpenAsync(pathA);
        await using (withOther.Db)
        {
            Assert.Null(withOther.LaterWorkElsewherePath);
            Assert.Equal("A의 번역", Assert.Single(withOther.Entries).DestText);
        }
    }

    // R10: For legacy already-split projects (both A and B DBs exist, B's plugin missing, same SHA), opening A reports
    // LaterWorkElsewherePath = B without modifying A's DB when B is newer, and reports nothing when A is newer.
    [Fact]
    public async Task R10_AlreadySplitProjects_NotifiesOnlyWhenMissingSiblingIsNewer_WithoutModifyingDb()
    {
        var pathA = WritePlugin("a");
        string dbPathA;
        var openedA = await OpenAsync(pathA);
        await using (openedA.Db)
        {
            dbPathA = openedA.Db.DbPath;
            var row = Assert.Single(openedA.Entries);
            await openedA.Db.UpdateStringTranslationAsync(row.Id, "A의 번역", StringEntryStatus.Edited, null, CancellationToken.None);
        }

        // Copy (not move) so both DBs stay present, reproducing the legacy split state.
        var pathB = WritePlugin("b");
        string dbPathB;
        var openedB = await OpenAsync(pathB);
        await using (openedB.Db)
        {
            dbPathB = openedB.Db.DbPath;
            var row = Assert.Single(openedB.Entries);
            await openedB.Db.UpdateStringTranslationAsync(row.Id, "B의 번역", StringEntryStatus.Edited, null, CancellationToken.None);
        }

        File.Delete(pathB);

        static void SetTimes(string dbPath, DateTime utc)
        {
            ProjectDb.ReleasePool(dbPath);
            if (File.Exists(dbPath)) File.SetLastWriteTimeUtc(dbPath, utc);
            if (File.Exists(dbPath + "-wal")) File.SetLastWriteTimeUtc(dbPath + "-wal", utc);
        }

        var baseTime = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SetTimes(dbPathA, baseTime);
        SetTimes(dbPathB, baseTime.AddMinutes(5));

        var reopenedWhenBNewer = await OpenAsync(pathA);
        await using (reopenedWhenBNewer.Db)
        {
            Assert.Equal(pathB, reopenedWhenBNewer.LaterWorkElsewherePath);
            Assert.Equal("A의 번역", Assert.Single(reopenedWhenBNewer.Entries).DestText);
            var notes = PluginOpenNotes.Describe(reopenedWhenBNewer);
            Assert.Contains(notes, n => n.Text == $"다른 위치({pathB})에서 더 나중에 작업한 프로젝트가 따로 남아 있습니다. 그때 번역한 줄은 이 프로젝트에 없을 수 있습니다.");
        }

        SetTimes(dbPathB, baseTime);
        SetTimes(dbPathA, baseTime.AddMinutes(10));

        var reopenedWhenANewer = await OpenAsync(pathA);
        await using (reopenedWhenANewer.Db)
        {
            Assert.Null(reopenedWhenANewer.LaterWorkElsewherePath);
            Assert.DoesNotContain(PluginOpenNotes.Describe(reopenedWhenANewer), n => n.Text.Contains("더 나중에 작업한 프로젝트", StringComparison.Ordinal));
        }
    }
}
