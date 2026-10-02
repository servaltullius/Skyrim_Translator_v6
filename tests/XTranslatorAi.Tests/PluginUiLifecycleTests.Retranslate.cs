using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task Retranslate_PutsFinishedRowsBackInTheQueue_AndTheNextStartTranslatesThemAgain()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "철검" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            var row = Assert.Single(fixture.Vm.Entries);
            Assert.Equal(StringEntryStatus.Done, row.Status);

            // Start alone does nothing for a finished row: this is why the command exists.
            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(handler.Requests);

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            Assert.True(fixture.Vm.RetranslateVisibleCommand.CanExecute(null));
            await fixture.Vm.RetranslateVisibleCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(StringEntryStatus.Pending, row.Status);
            Assert.Equal("", row.DestText);
            Assert.Equal(1, fixture.Vm.PendingCount);
            Assert.Equal(0, fixture.Vm.DoneCount);
            Assert.Empty(handler.Requests.Skip(1));

            handler.GeneratedText = "강철 검";
            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2, handler.Requests.Count);
            var saved = Assert.Single(await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal((StringEntryStatus.Done, "강철 검"), (saved.Status, saved.DestText));
            Assert.Equal("강철 검", row.DestText);
        });

    [Fact]
    public Task Retranslate_AsksAboutManualEdits_AndChangesNothingWhenCanceled()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            await fixture.Vm.CommitDestEditAsync(row, "내가 고친 철검");

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Cancel);
            await fixture.Vm.RetranslateSelectedCommand.ExecuteAsync(new List<object> { row }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal((StringEntryStatus.Edited, "내가 고친 철검"), (row.Status, row.DestText));

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.RetranslateSelectedCommand.ExecuteAsync(new List<object> { row }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(StringEntryStatus.Edited, row.Status);

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            await fixture.Vm.RetranslateSelectedCommand.ExecuteAsync(new List<object> { row }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal((StringEntryStatus.Pending, ""), (row.Status, row.DestText));
            var saved = Assert.Single(await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal(StringEntryStatus.Pending, saved.Status);
        });

    [Fact]
    public Task Retranslate_IsUnavailableWhileTranslating()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            Assert.True(fixture.Vm.RetranslateSelectedCommand.CanExecute(null));

            fixture.Vm.IsTranslating = true;

            Assert.False(fixture.Vm.RetranslateSelectedCommand.CanExecute(null));
            Assert.False(fixture.Vm.RetranslateVisibleCommand.CanExecute(null));
            fixture.Vm.IsTranslating = false;
        });
}
