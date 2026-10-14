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
}
