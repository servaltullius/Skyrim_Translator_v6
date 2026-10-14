using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// A row translated against its English on purpose (the modder pasted another record's text) kept the same warnings
/// on every quality check. Marking it "원문 오류 수정" takes it out of the check until it is unmarked or retranslated.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task MarkingASourceFix_ClearsItsWarnings_AndKeepsThemAwayOnTheNextScan()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Edited, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[1].Id, "Iron 검", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Edited);
            (rows[1].DestText, rows[1].Status) = ("Iron 검", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id);

            fixture.Vm.SelectedEntry = rows[0];
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);

            Assert.True(rows[0].IsSourceFix);
            Assert.DoesNotContain(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id);
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[1].Id && i.Code == "english_residue");
            Assert.True((await db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None)).ContainsKey(rows[0].Id));

            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            Assert.DoesNotContain(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id);

            fixture.Vm.SelectedEntry = rows[0];
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);

            Assert.False(rows[0].IsSourceFix);
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
            Assert.Empty(await db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None));
        });

    [Fact]
    public Task TheSourceFixButton_IsOffWithoutAProject()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();

            Assert.False(fixture.Vm.RetranslateSelectedCommand.CanExecute(null));
            Assert.False(fixture.Vm.ToggleSourceFixCommand.CanExecute(null));
        });

    /// <summary>
    /// Each tab binds to its own view model, which passes on the main view model's commands. A command the tab view
    /// model did not pass on bound to nothing: the button looked enabled and did nothing.
    /// </summary>
    [Theory]
    [InlineData("StringsTabView")]
    [InlineData("CompareTabView")]
    [InlineData("LqaTabView")]
    [InlineData("ProjectGlossaryTabView")]
    [InlineData("GlobalGlossaryTabView")]
    [InlineData("GlobalTranslationMemoryTabView")]
    [InlineData("PromptTabView")]
    [InlineData("ProjectContextTabView")]
    [InlineData("ApiLogsTabView")]
    public void TabCommands_ArePassedOnByTheTabViewModel(string view)
    {
        var xaml = File.ReadAllText(MainWindowSourcePath(Path.Combine("Views", view + ".xaml")));
        var tab = typeof(XTranslatorAi.App.ViewModels.MainViewModel).Assembly.GetType("XTranslatorAi.App.ViewModels.Tabs." + view + "Model", throwOnError: true)!;

        var missing = System.Text.RegularExpressions.Regex.Matches(xaml, @"Command=""\{Binding (?:Path=)?(\w+Command)[,}]")
            .Select(match => match.Groups[1].Value)
            .Where(name => tab.GetProperty(name) == null)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Opening an ESP built the grid's rows from the import without the rows' notes: the mark was in the project DB
    /// but the quality check, which reads it from the rows, flagged the marked rows again. The TM color was lost too.
    /// </summary>
    [Fact]
    public Task ReopeningAPlugin_KeepsTheRowsMarks()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var db = fixture.State.Db!;
            var row = Assert.Single(fixture.Vm.Entries);
            await db.UpdateStringTranslationAsync(row.Id, "일부러 다르게 고친 번역", StringEntryStatus.Edited, null, CancellationToken.None);
            await db.SetSourceFixAsync(row.Id, true, CancellationToken.None);
            await db.UpsertStringNoteAsync(row.Id, TranslationConstants.TmHitNoteKind, "TM 적용", CancellationToken.None);

            fixture.Ui.OpenPath = fixture.State.PluginDocument!.Info.InputPath;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            var reopened = Assert.Single(fixture.Vm.Entries);
            Assert.NotSame(row, reopened);
            Assert.Equal("일부러 다르게 고친 번역", reopened.DestText);
            Assert.True(reopened.IsSourceFix);
            Assert.True(reopened.IsTranslationMemoryApplied);
        });
}
