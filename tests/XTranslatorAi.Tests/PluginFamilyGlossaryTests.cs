using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Elden Rim is split into plugins in sibling folders, and every new plugin project started with an empty glossary:
/// War Ash Pack 2 and 3 came back with 습득 for "Learning" while the other files used 배우기.
/// </summary>
public sealed class PluginFamilyGlossaryTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-family-glossary", Guid.NewGuid().ToString("N"));
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

    private string WritePlugin(string folder, string name, string itemName)
    {
        var path = Path.Combine(_root, "mods", folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PluginProjectIntegrationTests.CreateMinimalPlugin(itemName));
        return path;
    }

    private async Task<ProjectWorkspaceService.LoadFromPluginResult> OpenAsync(string path)
        => await _workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(path, new PluginReadOptions(),
            "korean", "utf-8", "gemini-3.8-flash", "", false), CancellationToken.None);

    private async Task CreateProjectWithGlossaryAsync(string path, params (string Source, string Target, bool Enabled)[] terms)
    {
        var opened = await OpenAsync(path);
        await using (opened.Db)
        {
            await opened.Db.BulkInsertGlossaryAsync(terms.Select(t => ((string?)"eldenrim", t.Source, t.Target, t.Enabled, 10,
                (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)"Elden Rim 기준 용어")), CancellationToken.None);
        }
    }

    private static async Task<string[]> EnabledSourcesAsync(ProjectDb db)
        => (await db.GetGlossaryAsync(CancellationToken.None)).Where(g => g.Enabled).Select(g => g.SourceTerm).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task NewPluginOfTheSameMod_StartsWithItsEnabledGlossary()
    {
        await CreateProjectWithGlossaryAsync(WritePlugin(@"엘든림\0-Elden Rim-Base", "EldenSkyrim.esp", "Iron Sword"),
            ("Learning -", "배우기 -", true), ("Deathblow", "치명일격", true), ("Knockback", "밀쳐내기", false));
        await CreateProjectWithGlossaryAsync(WritePlugin(@"엘든림\1-Elden Rim-Weapon Art", "EldenSkyrim_RimSkills.esp", "Steel Sword"),
            ("Learning -", "배우기 -", true), ("Rim Scroll", "림 주문서", true));

        var opened = await OpenAsync(WritePlugin(@"엘든림\3-Elden Rim War Ash Pack02 1.5.3", "EldenWarAshPack2.esp", "Glass Sword"));
        await using (opened.Db)
        {
            Assert.Equal(new[] { "Deathblow", "Learning -", "Rim Scroll" }, await EnabledSourcesAsync(opened.Db));
            Assert.Equal(3, opened.InheritedGlossary.Count);
            Assert.Equal(new[] { "EldenSkyrim.esp", "EldenSkyrim_RimSkills.esp" }, opened.InheritedGlossary.FromPlugins.OrderBy(p => p, StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public async Task TermsTheModsProjectsTranslateDifferently_AreLeftOut()
    {
        await CreateProjectWithGlossaryAsync(WritePlugin(@"엘든림\0-Elden Rim-Base", "EldenSkyrim.esp", "Iron Sword"), ("Evoker", "소환사", true));
        await CreateProjectWithGlossaryAsync(WritePlugin(@"엘든림\1-Elden Rim-Weapon Art", "EldenSkyrim_RimSkills.esp", "Steel Sword"), ("Evoker", "이보커", true));

        var opened = await OpenAsync(WritePlugin(@"엘든림\2-Elden Rim War Ash Pack01", "EldenWarAshPack1.esp", "Glass Sword"));
        await using (opened.Db)
        {
            Assert.Empty(await EnabledSourcesAsync(opened.Db));
            Assert.Equal(0, opened.InheritedGlossary.Count);
        }
    }

    [Fact]
    public async Task OtherMods_AndExistingProjects_AreNotTouched()
    {
        var elden = WritePlugin(@"엘든림\0-Elden Rim-Base", "EldenSkyrim.esp", "Iron Sword");
        await CreateProjectWithGlossaryAsync(elden, ("Learning -", "배우기 -", true));

        var serana = await OpenAsync(WritePlugin(@"엘든림\Serana Dialogue Add-On", "SeranaDialogAddon.esp", "Steel Sword"));
        await using (serana.Db)
        {
            Assert.Empty(await EnabledSourcesAsync(serana.Db));
        }

        var reopened = await OpenAsync(elden);
        await using (reopened.Db)
        {
            Assert.Equal(0, reopened.InheritedGlossary.Count);
            Assert.Equal(new[] { "Learning -" }, await EnabledSourcesAsync(reopened.Db));
        }
    }
}
