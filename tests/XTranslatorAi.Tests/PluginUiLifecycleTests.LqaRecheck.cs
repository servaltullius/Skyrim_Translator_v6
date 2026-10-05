using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>The quality-check list kept the old translation's problems after the row was fixed and saved.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task SavingAFixedRow_UpdatesItsQualityCheckResults()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[1].Id, "Iron 검", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            (rows[1].DestText, rows[1].Status) = ("Iron 검", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = null;

            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[1].Id && i.Code == "english_residue");

            await fixture.Vm.CommitDestEditAsync(rows[0], "핸드 스트랩 공격");

            Assert.DoesNotContain(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[1].Id && i.Code == "english_residue");
        });

    /// <summary>The fixed issue left the list but stayed selected, so the tab went on pointing at a result no longer there.</summary>
    [Fact]
    public Task SavingTheSelectedIssuesFix_DropsTheSelection()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.SelectedLqaIssue = Assert.Single(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");

            await fixture.Vm.CommitDestEditAsync(rows[0], "핸드 스트랩 공격");

            Assert.True(fixture.Vm.SelectedLqaIssue == null || fixture.Vm.LqaIssues.Contains(fixture.Vm.SelectedLqaIssue));
        });

    /// <summary>
    /// A row saved while a scan ran was checked from the text the scan had read, and the re-check after the save was
    /// skipped because a scan was running, so the list showed the old text's problems.
    /// </summary>
    [Fact]
    public Task ARowSavedDuringAScan_IsCheckedAgainWhenTheScanEnds()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = null;

            // Save the fix at the scan's first progress report, after it has read the rows.
            Task? save = null;
            System.ComponentModel.PropertyChangedEventHandler saveOnScan = (_, e) =>
            {
                if (e.PropertyName == nameof(fixture.Vm.StatusMessage) && fixture.Vm.StatusMessage.Contains('%') && save == null)
                {
                    save = fixture.Vm.CommitDestEditAsync(rows[0], "핸드 스트랩 공격");
                }
            };
            fixture.Vm.PropertyChanged += saveOnScan;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            fixture.Vm.PropertyChanged -= saveOnScan;
            await save!;

            Assert.DoesNotContain(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
        });

    /// <summary>"후처리 재적용" and "태그 교정" rewrite many rows at once; their issues stayed as before until the next scan.</summary>
    [Fact]
    public Task RowsRewrittenByABulkTool_AreCheckedAgain()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");

            await fixture.Vm.RewriteFinishedRowsAsync(db, "test", "…", "done", row => row.Dest.Replace("Hand Strap", "핸드 스트랩"));

            Assert.Equal("핸드 스트랩 공격", rows[0].DestText);
            Assert.DoesNotContain(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
        });

    /// <summary>The full scan lists errors first; a re-check after a save re-sorted the whole list by row number.</summary>
    [Fact]
    public Task SavingARow_KeepsErrorsAtTheTopOfTheList()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Done);
            fixture.Vm.SelectedEntry = null;
            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);
            var error = new XTranslatorAi.App.ViewModels.LqaIssueViewModel(rows[1].Id, rows[1].OrderIndex, null, "WEAP:FULL",
                "Error", "token_mismatch", "m", "Iron Sword", "검");
            fixture.Vm.LqaIssues.ReplaceAll(new[] { error }.Concat(fixture.Vm.LqaIssues).ToList());

            await fixture.Vm.CommitDestEditAsync(rows[0], "Hand Strap 공격 개선");

            Assert.Equal("Error", fixture.Vm.LqaIssues[0].Severity);
            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
        });
}
