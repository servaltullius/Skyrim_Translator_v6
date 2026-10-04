using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>Keyboard review: Ctrl+Enter saves and moves on, F8/Shift+F8 jump between pending and failed rows.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task NextAndPreviousUnfinished_SkipFinishedRows_AndSaveAndNextMovesOn()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword", "Glass Sword");
            rows[1].Status = StringEntryStatus.Done;
            rows[2].Status = StringEntryStatus.Edited;
            rows[3].Status = StringEntryStatus.Error;
            fixture.Vm.SelectedEntry = rows[0];

            fixture.Vm.NextUnfinishedCommand.Execute(null);
            Assert.Same(rows[3], fixture.Vm.SelectedEntry);

            fixture.Vm.NextUnfinishedCommand.Execute(null);
            Assert.Same(rows[3], fixture.Vm.SelectedEntry);
            Assert.StartsWith("아래쪽에", fixture.Vm.StatusMessage);

            fixture.Vm.PreviousUnfinishedCommand.Execute(null);
            Assert.Same(rows[0], fixture.Vm.SelectedEntry);

            rows[0].EditableDestText = "철검";
            await fixture.Vm.SaveAndNextCommand.ExecuteAsync(null);
            Assert.Same(rows[1], fixture.Vm.SelectedEntry);
            await LeftRowCommits(fixture.Vm);
            var saved = await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal("철검", saved[0].DestText);
        });
}

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task SearchShowsHowManyRowsAreVisible()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Bow");
            fixture.Vm.SelectedEntry = null;
            Assert.Equal("전체 3행", fixture.Vm.VisibleEntrySummary);

            fixture.Vm.EntryFilterText = "Sword";
            Assert.Equal("표시 2 / 전체 3행", fixture.Vm.VisibleEntrySummary);
        });
}

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task SearchingANumber_FindsThatRowAndTextsWithTheNumber_NotLongerRowNumbers()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var sources = Enumerable.Range(0, 16).Select(i => i == 3 ? "Deals 15 damage" : $"Item {(char)('A' + i)}").ToArray();
            var rows = await LoadXmlWorkspaceAsync(fixture, sources);
            fixture.Vm.SelectedEntry = null;

            fixture.Vm.EntryFilterText = "15";

            // The text containing 15, row #15 and the row whose Id is 15 (#14); not #1, #10-#13 by digits alone.
            Assert.Equal(new[] { rows[3], rows[14], rows[15] }, VisibleRows(fixture.Vm));
        });
}
