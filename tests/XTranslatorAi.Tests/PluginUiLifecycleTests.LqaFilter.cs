using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.Tests;

/// <summary>
/// Information-only results (another speaker's tone, official names, TM fallbacks) can outnumber the warnings
/// many times over (442 on the pre-review Serana project), and the list had only a text search.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task HidingInformation_LeavesErrorsAndWarnings_AndCountsWhatIsShown()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            fixture.Vm.LqaIssues.ReplaceAll(new[]
            {
                new LqaIssueViewModel(1, 1, null, "INFO:NAM1", "Warn", "tone_inconsistent", "m", "s", "d"),
                new LqaIssueViewModel(2, 2, null, "INFO:NAM1", "Info", "tone_differs_from_plugin", "m", "s", "d"),
                new LqaIssueViewModel(3, 3, null, "BOOK:DESC", "Error", "token_mismatch", "m", "s", "d"),
            });
            fixture.Vm.LqaIssuesView.Refresh();
            Assert.Equal("전체 3건", fixture.Vm.LqaTab.LqaVisibleSummary);

            fixture.Vm.LqaTab.LqaHideInfo = true;

            Assert.Equal(new long[] { 1, 3 }, fixture.Vm.LqaIssuesView.Cast<LqaIssueViewModel>().Select(i => i.Id));
            Assert.Equal("표시 2 / 전체 3건", fixture.Vm.LqaTab.LqaVisibleSummary);
        });

    /// <summary>A scan selected its first result even when "참고 숨기기" hid it, so the editor showed a row not in the list.</summary>
    [Fact]
    public Task Scan_WithInformationHidden_SelectsOnlyAVisibleResult()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "철 검", XTranslatorAi.Core.Models.StringEntryStatus.Done, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("철 검", XTranslatorAi.Core.Models.StringEntryStatus.Done);
            await db.UpsertStringNoteAsync(rows[0].Id, "tm_fallback", "TM 대신 번역", CancellationToken.None);
            fixture.Vm.SelectedEntry = null;
            fixture.Vm.LqaTab.LqaHideInfo = true;

            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);

            Assert.Contains(fixture.Vm.LqaIssues, i => i.Severity == "Info");
            Assert.Empty(fixture.Vm.LqaIssuesView.Cast<LqaIssueViewModel>());
            Assert.Null(fixture.Vm.SelectedLqaIssue);
        });

    /// <summary>The compare slots kept one row's translations under the next row's summary.</summary>
    [Fact]
    public Task ChoosingAnotherRow_ClearsCompareOutputsOfTheEarlierRow()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword");
            fixture.Vm.SelectedEntry = rows[0];
            (fixture.Vm.Compare1Status, fixture.Vm.Compare1Output) = ("완료", "철검");

            // A refresh that briefly clears the selection keeps them.
            fixture.Vm.SelectedEntry = null;
            fixture.Vm.SelectedEntry = rows[0];
            Assert.Equal("철검", fixture.Vm.Compare1Output);

            fixture.Vm.SelectedEntry = rows[1];
            Assert.Equal(("", ""), (fixture.Vm.Compare1Status, fixture.Vm.Compare1Output));
        });

    /// <summary>
    /// Choosing another row kept the issue list's selection on the earlier row's issue: the tab showed that issue
    /// above the other row's text, and clicking it again did nothing because it was already selected.
    /// </summary>
    [Fact]
    public Task ChoosingAnotherRow_MovesTheIssueSelectionToThatRow()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Iron Axe");
            var first = new LqaIssueViewModel(rows[0].Id, rows[0].OrderIndex, null, "WEAP:FULL", "Warn", "untranslated", "m", "s", "");
            var second = new LqaIssueViewModel(rows[1].Id, rows[1].OrderIndex, null, "WEAP:FULL", "Warn", "untranslated", "m", "s", "");
            fixture.Vm.LqaIssues.ReplaceAll(new[] { first, second });
            fixture.Vm.LqaIssuesView.Refresh();
            fixture.Vm.SelectedLqaIssue = first;
            Assert.Same(rows[0], fixture.Vm.SelectedEntry);

            fixture.Vm.SelectedEntry = rows[1];
            Assert.Same(second, fixture.Vm.SelectedLqaIssue);

            fixture.Vm.SelectedEntry = rows[2];
            Assert.Null(fixture.Vm.SelectedLqaIssue);

            fixture.Vm.SelectedLqaIssue = first;
            Assert.Same(rows[0], fixture.Vm.SelectedEntry);
        });
}
