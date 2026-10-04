using System.ComponentModel;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// The glossary, TM and quality-check lists hold rows of the project (or game DB) they were read from. Replacing
/// the project without reloading them left those rows on screen over the new DB, where row Ids restart at 1.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task PluginOpenCancelledLate_DoesNotLeaveThePreviousProjectsListsOverTheNewDb()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            await AddListRowsAsync(fixture.Vm);
            fixture.Vm.SelectedGlossaryEntry = Assert.Single(fixture.Vm.Glossary, g => g.SourceTerm == "Old Term");
            Assert.Single(fixture.Vm.GlobalGlossary, g => g.SourceTerm == "Whiterun");
            Assert.Single(fixture.Vm.FranchiseTranslationMemory, e => e.SourceText == "Dragonborn");

            var path = await WriteReplacementPluginAsync(fixture);
            try
            {
                // Cancel right after the new project's rows are adopted, before its lists are reloaded.
                PropertyChangedEventHandler cancelOnLoad = (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.IsProjectLoaded) && fixture.Vm.IsProjectLoaded)
                    {
                        fixture.Vm.CancelPluginIoCommand.Execute(null);
                    }
                };
                fixture.Vm.PropertyChanged += cancelOnLoad;
                await fixture.Vm.OpenDroppedFileAsync(path).WaitAsync(TimeSpan.FromSeconds(10));
                fixture.Vm.PropertyChanged -= cancelOnLoad;

                Assert.Equal(path, fixture.State.PluginDocument!.Info.InputPath);
                Assert.Contains("보조 데이터 새로고침을 중지했습니다", fixture.Vm.StatusMessage);
                Assert.DoesNotContain(fixture.Vm.Glossary, g => g.SourceTerm == "Old Term");
                Assert.Null(fixture.Vm.SelectedGlossaryEntry);
                Assert.Empty(fixture.Vm.GlobalGlossary);
                Assert.Empty(fixture.Vm.FranchiseTranslationMemory);
            }
            finally
            {
                await ReleaseReplacementPluginDbAsync(fixture, path);
            }
        });

    [Fact]
    public Task OpeningAnotherProject_ClearsQualityCheckAndCompareResults()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var oldRow = (await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword"))[0];
            var issue = new LqaIssueViewModel(oldRow.Id, oldRow.OrderIndex, oldRow.Edid, oldRow.Rec, "Warn", "untranslated",
                "번역문이 비어 있습니다.", oldRow.SourceText, "");
            fixture.Vm.LqaIssues.Add(issue);
            fixture.Vm.SelectedLqaIssue = issue;
            fixture.Vm.Compare1Status = "Done";
            fixture.Vm.Compare1Output = "철검";
            Assert.True(fixture.Vm.ClearLqaCommand.CanExecute(null));

            var path = await WriteReplacementPluginAsync(fixture);
            try
            {
                await fixture.Vm.OpenDroppedFileAsync(path).WaitAsync(TimeSpan.FromSeconds(10));

                // The new project's first row has the same Id; the old issue would have selected it.
                Assert.Equal(oldRow.Id, Assert.Single(fixture.Vm.Entries).Id);
                Assert.Empty(fixture.Vm.LqaIssues);
                Assert.Null(fixture.Vm.SelectedLqaIssue);
                Assert.False(fixture.Vm.ClearLqaCommand.CanExecute(null));
                Assert.Equal(("", ""), (fixture.Vm.Compare1Status, fixture.Vm.Compare1Output));
            }
            finally
            {
                await ReleaseReplacementPluginDbAsync(fixture, path);
            }
        });

    /// <summary>One project term, one global term and one series TM row, added the way the tabs add them.</summary>
    private static async Task AddListRowsAsync(MainViewModel vm)
    {
        vm.GlossarySourceTerm = "Old Term";
        vm.GlossaryTargetTerm = "옛 용어";
        await vm.AddGlossaryCommand.ExecuteAsync(null);
        vm.GlobalGlossarySourceTerm = "Whiterun";
        vm.GlobalGlossaryTargetTerm = "화이트런";
        await vm.AddGlobalGlossaryCommand.ExecuteAsync(null);
        vm.FranchiseTranslationMemorySourceText = "Dragonborn";
        vm.FranchiseTranslationMemoryDestText = "드래곤본";
        await vm.AddFranchiseTranslationMemoryCommand.ExecuteAsync(null);
    }

    private static async Task<string> WriteReplacementPluginAsync(Fixture fixture)
    {
        var path = Path.Combine(fixture.Root, "replacement", "Replacement.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, PluginProjectIntegrationTests.CreateMinimalPlugin());
        return path;
    }

    /// <summary>The opened plugin has its own project DB; release it so the fixture can delete its folder.</summary>
    private static async Task ReleaseReplacementPluginDbAsync(Fixture fixture, string path)
    {
        if (fixture.State.PluginDocument?.Info.InputPath == path)
        {
            await fixture.State.DisposeDbAsync();
        }

        TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(
            ProjectPaths.GetPluginProjectDbPath(path, new PluginReadOptions(), "korean", "utf-8", fixture.Root));
    }
}
