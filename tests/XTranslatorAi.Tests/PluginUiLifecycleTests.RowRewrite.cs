using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// "후처리 재적용" and "태그 교정" rewrote rows a person had corrected (직접 수정) without asking. They now ask when
/// such rows would change: 예 includes them, 아니요 changes only finished machine rows, 취소 changes nothing.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Theory]
    [InlineData(UiMessageBoxResult.Yes, "반격 강화", "신비 강화")]
    [InlineData(UiMessageBoxResult.No, "반격 강화", "강화 신비")]
    [InlineData(UiMessageBoxResult.Cancel, "강화 반격", "강화 신비")]
    public Task ReapplyingPostEdits_AsksBeforeChangingEditedRows(UiMessageBoxResult answer, string done, string edited)
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Fortify Counter", "Fortify Mystic");
            var db = fixture.State.Db!;
            fixture.Vm.SelectedEntry = null;
            await db.UpdateStringTranslationAsync(rows[0].Id, "강화 반격", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[1].Id, "강화 신비", StringEntryStatus.Edited, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("강화 반격", StringEntryStatus.Done);
            (rows[1].DestText, rows[1].Status) = ("강화 신비", StringEntryStatus.Edited);

            fixture.Ui.Responses.Enqueue(answer);
            await fixture.Vm.ReapplyPostEditsCommand.ExecuteAsync(null);

            Assert.True(fixture.Ui.NetworkOrDialogUsed);
            var saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal((done, edited), (saved[0].DestText, saved[1].DestText));
            Assert.Equal((done, edited), (rows[0].DestText, rows[1].DestText));
            Assert.Equal(StringEntryStatus.Edited, saved[1].Status);
        });
}
