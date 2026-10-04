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
}
