using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
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
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

        // The earlier project is copied, not moved.
        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(_root, "projects"), "Test.*.sqlite", SearchOption.AllDirectories).Count());
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
}
