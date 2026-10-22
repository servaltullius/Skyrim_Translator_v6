using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// Results left as they are on purpose came back on every quality check among the ones still to fix (16 warnings
/// on Serana Dialogue Add-On). "확인함으로 표시" hides a result until its row's translation changes.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    private static async Task<List<StringEntryViewModel>> LoadTranslatedRowsAsync(Fixture fixture, params (string Source, string Dest)[] rows)
    {
        var loaded = await LoadXmlWorkspaceAsync(fixture, rows.Select(r => r.Source).ToArray());
        var db = fixture.State.Db!;
        for (var i = 0; i < rows.Length; i++)
        {
            await db.UpdateStringTranslationAsync(loaded[i].Id, rows[i].Dest, StringEntryStatus.Done, null, CancellationToken.None);
            (loaded[i].DestText, loaded[i].Status) = (rows[i].Dest, StringEntryStatus.Done);
            loaded[i].MarkDestTextSaved(rows[i].Dest);
        }

        return loaded.ToList();
    }

    private static LqaIssueViewModel Residue(Fixture fixture, long id)
        => Assert.Single(fixture.Vm.LqaIssues, i => i.Id == id && i.Code == "english_residue");

    private static IReadOnlyList<LqaIssueViewModel> Shown(Fixture fixture) => fixture.Vm.LqaIssuesView.Cast<LqaIssueViewModel>().ToList();

    [Fact]
    public Task MarkingAResultReviewed_HidesIt_KeepsItOnTheNextScan_AndSelectsTheNextResult()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadTranslatedRowsAsync(fixture, ("Iron Sword", "Iron 검"), ("Iron Axe", "Iron 도끼"));
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.SelectedLqaIssue = Residue(fixture, rows[0].Id);

            await fixture.Vm.LqaTab.ToggleLqaReviewedCommand.ExecuteAsync(null);

            Assert.True(Residue(fixture, rows[0].Id).IsReviewed);
            Assert.DoesNotContain(Shown(fixture), i => i.Id == rows[0].Id && i.Code == "english_residue");
            Assert.Equal(rows[1].Id, fixture.Vm.SelectedLqaIssue?.Id);
            Assert.EndsWith("(확인함 1건)", fixture.Vm.LqaTab.LqaVisibleSummary);
            Assert.Equal("Iron 검", (await fixture.State.Db!.GetLqaReviewedMarksAsync(CancellationToken.None))[(rows[0].Id, "english_residue")]);

            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);

            Assert.True(Residue(fixture, rows[0].Id).IsReviewed);
            Assert.Contains("확인함 1건", fixture.Vm.StatusMessage);
            Assert.DoesNotContain(Shown(fixture), i => i.Id == rows[0].Id && i.Code == "english_residue");

            // Shown with its mark once the box is cleared.
            fixture.Vm.LqaTab.LqaHideReviewed = false;
            Assert.Equal("확인함", Assert.Single(Shown(fixture), i => i.Id == rows[0].Id && i.Code == "english_residue").ReviewedDisplay);
        });

    [Fact]
    public Task AReviewedResult_ComesBack_WhenItsTranslationIsSavedWithOtherText()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadTranslatedRowsAsync(fixture, ("Iron Sword", "Iron 검"), ("Iron Axe", "Iron 도끼"));
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.SelectedLqaIssue = Residue(fixture, rows[0].Id);
            await fixture.Vm.LqaTab.ToggleLqaReviewedCommand.ExecuteAsync(null);

            // A rewording that keeps only the English looked at keeps the mark (ProjectDbLqaReviewedTests); a new
            // English word is something nobody has looked at.
            fixture.Vm.SelectedEntry = rows[0];
            rows[0].EditableDestText = "Iron Long 검";
            await fixture.Vm.SaveSelectedDestCommand.ExecuteAsync(null);

            Assert.False(Residue(fixture, rows[0].Id).IsReviewed);
            Assert.Contains(Shown(fixture), i => i.Id == rows[0].Id && i.Code == "english_residue");

            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            Assert.False(Residue(fixture, rows[0].Id).IsReviewed);
        });

    [Fact]
    public Task MarkingReviewedResultsAgain_TakesTheMarksOff()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadTranslatedRowsAsync(fixture, ("Iron Sword", "Iron 검"), ("Iron Axe", "Iron 도끼"));
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.LqaTab.LqaHideReviewed = false;
            var both = new System.Collections.ArrayList { Residue(fixture, rows[0].Id), Residue(fixture, rows[1].Id) };
            await fixture.Vm.LqaTab.ToggleLqaReviewedCommand.ExecuteAsync(both);
            Assert.Equal(2, (await fixture.State.Db!.GetLqaReviewedMarksAsync(CancellationToken.None)).Count);

            await fixture.Vm.LqaTab.ToggleLqaReviewedCommand.ExecuteAsync(both);

            Assert.All(fixture.Vm.LqaIssues, i => Assert.False(i.IsReviewed));
            Assert.Empty(await fixture.State.Db!.GetLqaReviewedMarksAsync(CancellationToken.None));
            Assert.Equal("전체 2건", fixture.Vm.LqaTab.LqaVisibleSummary);
        });

    // The button reads "확인함 표시 해제" on a marked current result; with an unmarked one picked beside it, the
    // command decided by the whole selection and marked both.
    [Fact]
    public Task TheReviewedButton_DoesWhatItSaysForTheCurrentResult()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadTranslatedRowsAsync(fixture, ("Iron Sword", "Iron 검"), ("Iron Axe", "Iron 도끼"));
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.LqaTab.LqaHideReviewed = false;
            fixture.Vm.SelectedLqaIssue = Residue(fixture, rows[0].Id);
            await fixture.Vm.LqaTab.ToggleLqaReviewedCommand.ExecuteAsync(null);
            Assert.True(Residue(fixture, rows[0].Id).IsReviewed);

            fixture.Vm.SelectedLqaIssue = Residue(fixture, rows[0].Id);
            await fixture.Vm.LqaTab.ToggleLqaReviewedCommand.ExecuteAsync(new System.Collections.ArrayList { Residue(fixture, rows[0].Id), Residue(fixture, rows[1].Id) });

            Assert.All(fixture.Vm.LqaIssues, i => Assert.False(i.IsReviewed));
            Assert.Empty(await fixture.State.Db!.GetLqaReviewedMarksAsync(CancellationToken.None));
        });

    [Fact]
    public Task TheReviewedButton_IsOffWithoutAProject()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();

            Assert.False(fixture.Vm.LqaTab.ToggleLqaReviewedCommand.CanExecute(null));
        });
}
