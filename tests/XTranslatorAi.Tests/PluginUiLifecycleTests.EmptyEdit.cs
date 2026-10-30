using XTranslatorAi.App.Services;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// An emptied translation was saved as a hand edit of nothing: later runs skip it and the export writes it empty, and
/// the save confirmation did not count it. Saving one asks whether to keep it empty or put the row back to 대기.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task EmptyingATranslation_AsksToKeepItEmpty_OrPutTheRowBackToPending()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword");
            var db = fixture.State.Db!;
            foreach (var (row, text) in rows.Zip(new[] { "내가 고친 철검", "강철 검", "엘프 검" }))
            {
                Assert.Equal(MainViewModel.DestEditOutcome.Saved, await fixture.Vm.CommitDestEditAsync(row, text));
            }

            // 취소: nothing saved, the editor keeps the emptied text.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Cancel);
            rows[0].EditableDestText = "";
            Assert.Equal(MainViewModel.DestEditOutcome.NotSaved, await fixture.Vm.CommitDestEditAsync(rows[0], rows[0].DestText));
            Assert.Contains(fixture.Ui.Titles, title => title == "빈 번역 저장");
            Assert.True(rows[0].HasUnsavedDestEdit);
            var saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Edited, "내가 고친 철검"), (saved[0].Status, saved[0].DestText));

            // 아니요: back to 대기, the hand edit's old text noted so the project TM does not fill it back.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            Assert.Equal(MainViewModel.DestEditOutcome.BackToPending, await fixture.Vm.CommitDestEditAsync(rows[0], rows[0].DestText));
            saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Pending, ""), (saved[0].Status, saved[0].DestText));
            Assert.Equal((StringEntryStatus.Pending, false), (rows[0].Status, rows[0].HasUnsavedDestEdit));
            Assert.Equal("내가 고친 철검", (await db.GetStringNotesByKindAsync(TranslationConstants.RetranslatedEditNoteKind, CancellationToken.None))[rows[0].Id]);

            // Leaving an emptied row asks too, after the selection has moved.
            fixture.Vm.SelectedEntry = rows[2];
            rows[2].EditableDestText = "";
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            fixture.Vm.SelectedEntry = rows[1];
            Assert.Same(rows[1], fixture.Vm.SelectedEntry);
            await LeftRowCommits(fixture.Vm);
            Assert.Equal(StringEntryStatus.Pending, rows[2].Status);
            Assert.Equal(StringEntryStatus.Pending, (await db.GetStringsAsync(10, 0, CancellationToken.None))[2].Status);
            Assert.Contains("#2 행을 대기로", fixture.Vm.StatusMessage);

            // 예: an empty hand edit, as before.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            Assert.Equal(MainViewModel.DestEditOutcome.Saved, await fixture.Vm.CommitDestEditAsync(rows[1], ""));
            saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Edited, ""), (saved[1].Status, saved[1].DestText));
        });

    /// <summary>Saving before export does not go on past an emptied translation the user kept in the editor.</summary>
    [Fact]
    public Task ExportingXml_StopsAtAnEmptiedTranslationNotSaved_AndCountsEmptyHandEdits()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword");
            await fixture.Vm.CommitDestEditAsync(rows[0], "철검");
            await fixture.Vm.CommitDestEditAsync(rows[1], "강철 검");
            var exportPath = Path.Combine(fixture.Root, "out.xml");
            fixture.Ui.SavePath = exportPath;

            rows[0].EditableDestText = "";
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Cancel);
            await fixture.Vm.ExportXmlCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(File.Exists(exportPath));
            Assert.DoesNotContain("저장 확인", fixture.Ui.Titles);

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.Yes);
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.ExportXmlCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(fixture.Ui.Messages, m => m.Contains("번역문을 비워 둔 직접 수정 행이 1개") && !m.Contains("오류 0개"));
            Assert.False(File.Exists(exportPath));
            Assert.Contains("'직접 수정'", fixture.Vm.StatusMessage);
        });
}
