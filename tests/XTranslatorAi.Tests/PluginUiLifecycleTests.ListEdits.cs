using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.Tests;

/// <summary>
/// Glossary and TM grid edits stay unsaved (변경됨) until "저장". Adding a term, importing a file or opening a project
/// reloads those lists and used to drop the edits without asking; they now ask to save first or cancel.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task AddingATerm_WithUnsavedGlossaryEdits_AsksFirst_AndSavesThemOrCancels()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            var db = fixture.State.Db!;
            fixture.Vm.GlossarySourceTerm = "Iron";
            fixture.Vm.GlossaryTargetTerm = "철";
            await fixture.Vm.AddGlossaryCommand.ExecuteAsync(null);
            Assert.False(fixture.Ui.NetworkOrDialogUsed);
            var iron = Assert.Single(fixture.Vm.Glossary);
            iron.TargetTerm = "철제";
            Assert.True(iron.IsDirty);

            fixture.Vm.GlossarySourceTerm = "Steel";
            fixture.Vm.GlossaryTargetTerm = "강철";
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.AddGlossaryCommand.ExecuteAsync(null);

            Assert.True(fixture.Ui.NetworkOrDialogUsed);
            Assert.Same(iron, Assert.Single(fixture.Vm.Glossary));
            Assert.True(iron.IsDirty);
            Assert.Equal(new[] { ("Iron", "철") }, Terms(await db.GetGlossaryAsync(CancellationToken.None)));
            Assert.Equal("Steel", fixture.Vm.GlossarySourceTerm);

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            await fixture.Vm.AddGlossaryCommand.ExecuteAsync(null);

            Assert.Equal(new[] { ("Iron", "철제"), ("Steel", "강철") }, Terms(await db.GetGlossaryAsync(CancellationToken.None)));
            Assert.Equal(2, fixture.Vm.Glossary.Count);
            Assert.DoesNotContain(fixture.Vm.Glossary, g => g.IsDirty);
        });

    [Fact]
    public Task AddingToTheSeriesTm_WithUnsavedTmEdits_AsksFirst_AndSavesThemOrCancels()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            await AddListRowsAsync(fixture.Vm);
            var dragonborn = Assert.Single(fixture.Vm.FranchiseTranslationMemory);
            dragonborn.DestText = "용기사";

            fixture.Vm.FranchiseTranslationMemorySourceText = "Whiterun";
            fixture.Vm.FranchiseTranslationMemoryDestText = "화이트런";
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.AddFranchiseTranslationMemoryCommand.ExecuteAsync(null);
            Assert.Same(dragonborn, Assert.Single(fixture.Vm.FranchiseTranslationMemory));
            Assert.True(dragonborn.IsDirty);

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            await fixture.Vm.AddFranchiseTranslationMemoryCommand.ExecuteAsync(null);
            Assert.Equal(new[] { ("Dragonborn", "용기사"), ("Whiterun", "화이트런") },
                fixture.Vm.FranchiseTranslationMemory.Select(e => (e.SourceText, e.DestText)).OrderBy(p => p.SourceText));
            Assert.DoesNotContain(fixture.Vm.FranchiseTranslationMemory, e => e.IsDirty);
        });

    [Fact]
    public Task OpeningAnotherFile_WithUnsavedGlossaryAndTmEdits_AsksFirst_AndSavesThemOrCancels()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var originalDb = fixture.State.Db;
            await AddListRowsAsync(fixture.Vm);
            Assert.Single(fixture.Vm.Glossary, g => g.SourceTerm == "Old Term").TargetTerm = "고친 용어";
            Assert.Single(fixture.Vm.GlobalGlossary).TargetTerm = "화이트런 시";
            Assert.Single(fixture.Vm.FranchiseTranslationMemory).DestText = "용기사";

            var path = await WriteReplacementPluginAsync(fixture);
            try
            {
                fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
                await fixture.Vm.OpenDroppedFileAsync(path).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(originalDb, fixture.State.Db);
                Assert.True(fixture.Vm.IsWorkspaceInteractive);
                Assert.True(Assert.Single(fixture.Vm.GlobalGlossary).IsDirty);

                fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
                await fixture.Vm.OpenDroppedFileAsync(path).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(path, fixture.State.PluginDocument!.Info.InputPath);
                // The reloaded global lists show the saved edits.
                Assert.Equal(("Whiterun", "화이트런 시"), (fixture.Vm.GlobalGlossary.Single().SourceTerm, fixture.Vm.GlobalGlossary.Single().TargetTerm));
                Assert.Equal("용기사", Assert.Single(fixture.Vm.FranchiseTranslationMemory).DestText);
                await using var previous = await ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
                Assert.Contains(("Old Term", "고친 용어"), Terms(await previous.GetGlossaryAsync(CancellationToken.None)));
            }
            finally
            {
                await ReleaseReplacementPluginDbAsync(fixture, path);
            }
        });

    private static IEnumerable<(string, string)> Terms(IEnumerable<XTranslatorAi.Core.Text.GlossaryEntry> entries)
        => entries.Select(e => (e.SourceTerm, e.TargetTerm)).OrderBy(p => p.SourceTerm, StringComparer.Ordinal);
}
