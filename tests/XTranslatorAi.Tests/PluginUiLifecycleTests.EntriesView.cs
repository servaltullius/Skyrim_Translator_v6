using System.Windows.Threading;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// The strings grid filters live on the translation text. Fixing the selected row under a search or
/// "보호 요소 불일치만" used to drop it from the grid while the user was still typing, which cleared the selection
/// and blanked the editor. The row now stays until another row is selected.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task EditingTheSelectedRow_KeepsItInTheFilteredGrid_UntilAnotherRowIsSelected()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword");
            rows[0].DestText = "철검";
            rows[1].DestText = "철검 두 자루";
            fixture.Vm.EntryFilterText = "철검";
            Assert.Equal(new[] { rows[0], rows[1] }, VisibleRows(fixture.Vm));
            Assert.Same(rows[0], fixture.Vm.SelectedEntry);

            // The typed text no longer contains the search term; the row being edited must not vanish.
            rows[0].EditableDestText = "쇠로 만든 검";
            PumpDispatcher();
            Assert.Equal(new[] { rows[0], rows[1] }, VisibleRows(fixture.Vm));
            Assert.Same(rows[0], fixture.Vm.SelectedEntry);

            // Leaving the row saves it and filters it like any other row.
            fixture.Vm.SelectedEntry = rows[1];
            await LeftRowCommits(fixture.Vm);
            PumpDispatcher();
            Assert.Equal(new[] { rows[1] }, VisibleRows(fixture.Vm));
            var saved = await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Edited, "쇠로 만든 검"), (saved[0].Status, saved[0].DestText));

            // Changing the filter itself applies to the selected row too, which then is no longer open.
            fixture.Vm.EntryFilterText = "Elven";
            Assert.Equal(new[] { rows[2] }, VisibleRows(fixture.Vm));
            Assert.Null(fixture.Vm.SelectedEntry);
            Assert.False(rows[1].IsOpenInEditor);
        });

    [Fact]
    public Task FixingTheSelectedRow_UnderTheMismatchFilter_KeepsItUntilAnotherRowIsSelected()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Take <Alias=Player>'s sword.", "Give <Alias=Player> gold.");
            fixture.Vm.EntryFilterTagMismatchOnly = true;
            Assert.Equal(new[] { rows[0], rows[1] }, VisibleRows(fixture.Vm));

            rows[0].EditableDestText = "<Alias=Player>의 검을 가져가라.";
            PumpDispatcher();
            Assert.Equal(new[] { rows[0], rows[1] }, VisibleRows(fixture.Vm));

            fixture.Vm.SelectedEntry = rows[1];
            await LeftRowCommits(fixture.Vm);
            PumpDispatcher();
            Assert.Equal(new[] { rows[1] }, VisibleRows(fixture.Vm));
        });

    // Without WPF live filtering (3-5 s per search keystroke on 67,390 rows), rows that change are filtered one by one.
    [Fact]
    public Task RowsChangingUnderAStatusFilter_LeaveAndJoinTheGrid()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword");
            fixture.Vm.SelectedEntry = null;
            fixture.Vm.EntryFilterStatus = StringEntryStatusLabels.ToLabel(StringEntryStatus.Pending);
            Assert.Equal(rows, VisibleRows(fixture.Vm));

            // Translated during a run: the row leaves the "대기" view at once.
            rows[1].Status = StringEntryStatus.Done;
            Assert.Equal(new[] { rows[0], rows[2] }, VisibleRows(fixture.Vm));

            // Back to pending (다시 번역): it returns after the queued refresh.
            rows[1].Status = StringEntryStatus.Pending;
            PumpDispatcher();
            Assert.Equal(rows, VisibleRows(fixture.Vm));
        });

    [Fact]
    public Task SearchMatchesTextChangedAfterTheFilterWasSet()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword");
            fixture.Vm.SelectedEntry = null;
            fixture.Vm.EntryFilterText = "강철";
            Assert.Empty(VisibleRows(fixture.Vm));

            rows[1].DestText = "강철 검";
            PumpDispatcher();
            Assert.Equal(new[] { rows[1] }, VisibleRows(fixture.Vm));
        });

    private static List<StringEntryViewModel> VisibleRows(MainViewModel vm)
        => vm.EntriesView.Cast<StringEntryViewModel>().ToList();

    /// <summary>Runs the WPF work queued on this thread, such as the grid's live filtering.</summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
