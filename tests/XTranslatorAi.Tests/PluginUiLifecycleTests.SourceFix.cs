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

    // Unmarking a row in a project never checked (or whose results were cleared) listed that row's results alone, read as
    // the whole project's ("전체 N건").
    [Fact]
    public Task UnmarkingASourceFix_WithoutAWholeCheck_ListsNothing()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = rows[0];

            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);

            Assert.False(rows[0].IsSourceFix);
            Assert.Empty(fixture.Vm.LqaIssues);

            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.ClearLqaCommand.Execute(null);
            fixture.Vm.SelectedEntry = rows[0];
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);

            Assert.Empty(fixture.Vm.LqaIssues);
        });

    // Retranslating deletes the row's mark in the DB; the row kept it on screen, the next checks still skipped the
    // row, and marking it again took two clicks (the first one "unmarked" it).
    [Fact]
    public Task Retranslating_ClearsTheSourceFixMarkOnScreenToo()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = rows[0];
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);
            Assert.True(rows[0].IsSourceFix);

            fixture.Ui.Responses.Enqueue(XTranslatorAi.App.Services.UiMessageBoxResult.Yes);
            await fixture.Vm.RetranslateSelectedCommand.ExecuteAsync(new List<object> { rows[0] }).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(StringEntryStatus.Pending, rows[0].Status);
            Assert.False(rows[0].IsSourceFix);
            await fixture.Vm.ToggleSourceFixCommand.ExecuteAsync(null);
            Assert.True(rows[0].IsSourceFix);
            Assert.True((await db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None)).ContainsKey(rows[0].Id));
        });

    // A 번역용어집 신규.md beside the input was imported without a dialog, into the global glossary too, and no other
    // file could be picked while it was there.
    [Fact]
    public Task GlossaryImport_AlwaysAsksForTheFile()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "번역용어집 신규.md"), "| Zzyzx Blade | 지직스 검 |\n");
            fixture.Ui.OpenPath = null;

            await fixture.Vm.ImportGlobalGlossaryCommand.ExecuteAsync(null);

            Assert.True(fixture.Ui.NetworkOrDialogUsed);
            var global = await fixture.GlobalDb.GetOrCreateAsync(CancellationToken.None);
            Assert.DoesNotContain(await global!.GetGlossaryAsync(CancellationToken.None), e => e.SourceTerm == "Zzyzx Blade");
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
