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
