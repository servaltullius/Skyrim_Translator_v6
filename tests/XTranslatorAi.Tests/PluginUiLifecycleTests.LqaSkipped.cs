using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// An xTranslator XML opens its existing translations as 건너뜀 (Skipped), and the quality check looked only at done
/// and edited rows, so checking someone else's translation said "문제를 찾지 못했습니다" (LotD XML: 1,424 issues
/// when the same rows are checked as translated). Decided 2026-10-05 to check them.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task QualityCheck_IncludesTranslationsImportedAsSkipped()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Hand Strap Attack", "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[0].Id, "Hand Strap 공격", StringEntryStatus.Skipped, null, CancellationToken.None);
            (rows[0].DestText, rows[0].Status) = ("Hand Strap 공격", StringEntryStatus.Skipped);
            // A row skipped on purpose keeps its source: nothing to check.
            await db.UpdateStringTranslationAsync(rows[1].Id, "Iron Sword", StringEntryStatus.Skipped, null, CancellationToken.None);
            (rows[1].DestText, rows[1].Status) = ("Iron Sword", StringEntryStatus.Skipped);
            fixture.Vm.SelectedEntry = null;

            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null);

            Assert.Contains(fixture.Vm.LqaIssues, i => i.Id == rows[0].Id && i.Code == "english_residue");
            Assert.DoesNotContain(fixture.Vm.LqaIssues, i => i.Id == rows[1].Id);
        });
}
