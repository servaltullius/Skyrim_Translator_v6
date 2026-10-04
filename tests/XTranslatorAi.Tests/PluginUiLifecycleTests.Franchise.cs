using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Xml.Linq;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// 게임 시리즈 picks the global DB behind 전체 용어집 and 시리즈 TM. Changing it used to switch the DB without
/// reloading the lists while translating or with no project open, so saving or deleting sent the previous game's
/// row Ids to the new game's DB, where they changed unrelated rows.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task FranchiseChange_WithoutAProject_ReloadsBothListsFromTheNewGame_AndSavesThere()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await using var games = await GameDbs.CreateAsync(fixture);
            // The series TM tab works without a project.
            await fixture.Vm.ReloadFranchiseTranslationMemoryCommand.ExecuteAsync(null);
            Assert.Equal("Dragonborn", Assert.Single(fixture.Vm.FranchiseTranslationMemory).SourceText);

            fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;
            await FranchiseReload(fixture.Vm);

            Assert.Equal("Vault-Tec", Assert.Single(fixture.Vm.GlobalGlossary).SourceTerm);
            var pipBoy = Assert.Single(fixture.Vm.FranchiseTranslationMemory);
            Assert.Equal("Pip-Boy", pipBoy.SourceText);

            pipBoy.DestText = "핍보이 3000";
            await fixture.Vm.SaveFranchiseTranslationMemoryChangesCommand.ExecuteAsync(null);
            Assert.Equal(("Pip-Boy", "핍보이 3000"), TmPair(Assert.Single(await games.TmAsync(games.Fallout))));
            Assert.Equal(("Dragonborn", "드래곤본"), TmPair(Assert.Single(await games.TmAsync(games.Skyrim))));
        });

    [Fact]
    public Task FranchiseChange_WithAProject_SavesGlobalGlossaryEditsIntoTheNewGamesDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await using var games = await GameDbs.CreateAsync(fixture);
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            await ReloadGlobalListsAsync(fixture.Vm);
            Assert.Equal("Whiterun", Assert.Single(fixture.Vm.GlobalGlossary).SourceTerm);

            fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;
            await FranchiseReload(fixture.Vm);

            var vaultTec = Assert.Single(fixture.Vm.GlobalGlossary);
            Assert.Equal("Vault-Tec", vaultTec.SourceTerm);
            vaultTec.TargetTerm = "볼트-텍";
            await fixture.Vm.SaveGlobalGlossaryChangesCommand.ExecuteAsync(null);

            Assert.Equal(("Vault-Tec", "볼트-텍"), TermPair(Assert.Single(await games.Fallout.GetGlossaryAsync(CancellationToken.None))));
            Assert.Equal(("Whiterun", "화이트런"), TermPair(Assert.Single(await games.Skyrim.GetGlossaryAsync(CancellationToken.None))));
            Assert.Equal(BethesdaFranchise.Fallout, (await fixture.State.Db!.TryGetProjectAsync(CancellationToken.None))!.Franchise);
        });

    [Fact]
    public Task FranchiseChange_IsRefusedWhileTranslating_AndTheSelectorIsDisabled()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await using var games = await GameDbs.CreateAsync(fixture);
            var changed = new List<string?>();
            ((INotifyPropertyChanged)fixture.Vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            Assert.True(fixture.Vm.CanChangeFranchise);

            fixture.Vm.IsTranslating = true;
            Assert.False(fixture.Vm.CanChangeFranchise);
            Assert.Contains(nameof(MainViewModel.CanChangeFranchise), changed);
            fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;

            Assert.Equal(BethesdaFranchise.ElderScrolls, fixture.Vm.SelectedFranchise);
            Assert.Equal(BethesdaFranchise.ElderScrolls, games.Service.SelectedFranchise);
            Assert.Contains("번역 중에는 게임 시리즈를 바꿀 수 없습니다", fixture.Vm.StatusMessage);
            Assert.False(fixture.Ui.NetworkOrDialogUsed);
            fixture.Vm.IsTranslating = false;
            Assert.True(fixture.Vm.CanChangeFranchise);

            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var selector = Assert.Single(XDocument.Load(MainWindowSourcePath("MainWindow.xaml")).Root!.Descendants(wpf + "ComboBox"),
                box => (string?)box.Attribute("SelectedValue") == "{Binding SelectedFranchise}");
            Assert.Equal("{Binding CanChangeFranchise}", (string?)selector.Attribute("IsEnabled"));
        });

    [Fact]
    public Task FranchiseChange_WithUnsavedListEdits_AsksFirst_AndNeverSavesThemIntoTheOtherGame()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await using var games = await GameDbs.CreateAsync(fixture);
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            await ReloadGlobalListsAsync(fixture.Vm);
            var whiterun = Assert.Single(fixture.Vm.GlobalGlossary);
            whiterun.TargetTerm = "화이트런 시";

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;
            Assert.Equal(BethesdaFranchise.ElderScrolls, fixture.Vm.SelectedFranchise);
            Assert.Equal(BethesdaFranchise.ElderScrolls, games.Service.SelectedFranchise);
            Assert.Same(whiterun, Assert.Single(fixture.Vm.GlobalGlossary));
            Assert.True(whiterun.IsDirty);

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;
            await FranchiseReload(fixture.Vm);
            Assert.False(Assert.Single(fixture.Vm.GlobalGlossary).IsDirty);
            await fixture.Vm.SaveGlobalGlossaryChangesCommand.ExecuteAsync(null);

            Assert.Equal(("Vault-Tec", "볼트텍"), TermPair(Assert.Single(await games.Fallout.GetGlossaryAsync(CancellationToken.None))));
            Assert.Equal(("Whiterun", "화이트런"), TermPair(Assert.Single(await games.Skyrim.GetGlossaryAsync(CancellationToken.None))));
        });

    [Fact]
    public Task SavingBeforeTheFranchiseReloadFinishes_DoesNotSendThePreviousGamesIdsToTheNewDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await using var games = await GameDbs.CreateAsync(fixture);
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            await ReloadGlobalListsAsync(fixture.Vm);

            // Hold the Fallout DB so the reload started by the switch is still waiting when the user saves.
            var falloutGate = (SemaphoreSlim)typeof(ProjectDb).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(games.Fallout)!;
            await falloutGate.WaitAsync();
            Task glossarySave, tmSave;
            string statusAfterGlossarySave;
            try
            {
                fixture.Vm.SelectedFranchise = BethesdaFranchise.Fallout;
                Assert.Equal("Whiterun", Assert.Single(fixture.Vm.GlobalGlossary).SourceTerm);

                // Both Skyrim rows have Id 1, like the Fallout rows: saved now, they would overwrite Vault-Tec and Pip-Boy.
                Assert.Single(fixture.Vm.GlobalGlossary).TargetTerm = "화이트런 시";
                glossarySave = fixture.Vm.SaveGlobalGlossaryChangesCommand.ExecuteAsync(null);
                statusAfterGlossarySave = fixture.Vm.StatusMessage;
                Assert.Single(fixture.Vm.FranchiseTranslationMemory).DestText = "용기사";
                tmSave = fixture.Vm.SaveFranchiseTranslationMemoryChangesCommand.ExecuteAsync(null);
            }
            finally
            {
                falloutGate.Release();
            }

            await glossarySave;
            await tmSave;
            await FranchiseReload(fixture.Vm);
            Assert.Contains("이전 게임 시리즈", statusAfterGlossarySave);

            Assert.Equal(("Vault-Tec", "볼트텍"), TermPair(Assert.Single(await games.Fallout.GetGlossaryAsync(CancellationToken.None))));
            Assert.Equal(("Pip-Boy", "핍보이"), TmPair(Assert.Single(await games.TmAsync(games.Fallout))));
            Assert.Equal(("Whiterun", "화이트런"), TermPair(Assert.Single(await games.Skyrim.GetGlossaryAsync(CancellationToken.None))));
            Assert.Equal(("Dragonborn", "드래곤본"), TmPair(Assert.Single(await games.TmAsync(games.Skyrim))));
            Assert.Equal("Vault-Tec", Assert.Single(fixture.Vm.GlobalGlossary).SourceTerm);
            Assert.Equal("Pip-Boy", Assert.Single(fixture.Vm.FranchiseTranslationMemory).SourceText);
        });

    private static Task FranchiseReload(MainViewModel vm)
        => (Task)typeof(MainViewModel).GetField("_franchiseReload", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;

    private static async Task ReloadGlobalListsAsync(MainViewModel vm)
    {
        await (Task)typeof(MainViewModel).GetMethod("ReloadGlobalGlossaryAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, null)!;
        await vm.ReloadFranchiseTranslationMemoryCommand.ExecuteAsync(null);
    }

    private static (string, string) TermPair(GlossaryEntry entry) => (entry.SourceTerm, entry.TargetTerm);

    private static (string, string) TmPair(TranslationMemoryEntry entry) => (entry.SourceText, entry.DestText);

    /// <summary>
    /// Disposable Skyrim and Fallout global DBs in the service's cache, one term and one TM row each, both with Id 1.
    /// Commands must never open the user's real global DBs.
    /// </summary>
    private sealed class GameDbs : IAsyncDisposable
    {
        private readonly string[] _paths;
        public GlobalProjectDbService Service { get; }
        public ProjectDb Skyrim { get; }
        public ProjectDb Fallout { get; }

        private GameDbs(GlobalProjectDbService service, ProjectDb skyrim, ProjectDb fallout, string[] paths)
        {
            Service = service;
            Skyrim = skyrim;
            Fallout = fallout;
            _paths = paths;
        }

        public static async Task<GameDbs> CreateAsync(Fixture fixture)
        {
            var service = (GlobalProjectDbService)typeof(MainViewModel)
                .GetField("_globalProjectDbService", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Vm)!;
            var cache = (ConcurrentDictionary<BethesdaFranchise, ProjectDb>)typeof(GlobalProjectDbService)
                .GetField("_dbByFranchise", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
            var paths = new[] { Path.Combine(fixture.Root, "global-skyrim.sqlite"), Path.Combine(fixture.Root, "global-fallout.sqlite") };
            var skyrim = await CreateGameDbAsync(paths[0], "Whiterun", "화이트런", "Dragonborn", "드래곤본");
            var fallout = await CreateGameDbAsync(paths[1], "Vault-Tec", "볼트텍", "Pip-Boy", "핍보이");
            cache[BethesdaFranchise.ElderScrolls] = skyrim;
            cache[BethesdaFranchise.Fallout] = fallout;
            return new GameDbs(service, skyrim, fallout, paths);
        }

        public Task<IReadOnlyList<TranslationMemoryEntry>> TmAsync(ProjectDb db)
            => db.GetTranslationMemoryEntriesAsync("english", "korean", CancellationToken.None);

        private static async Task<ProjectDb> CreateGameDbAsync(string path, string term, string termKo, string tmSource, string tmDest)
        {
            var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await db.UpsertGlossaryAsync(new GlossaryUpsertRequest(null, term, termKo, true, 10,
                GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null), CancellationToken.None);
            await db.BulkUpsertTranslationMemoryAsync("english", "korean", new List<(string, string)> { (tmSource, tmDest) }, CancellationToken.None);
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            await Skyrim.DisposeAsync();
            await Fallout.DisposeAsync();
            foreach (var path in _paths)
            {
                TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
            }
        }
    }
}
