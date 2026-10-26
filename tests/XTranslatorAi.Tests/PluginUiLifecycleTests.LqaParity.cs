using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

/// <summary>
/// The GUI's quality check reads the grid's rows; the agent's reads the same rows from the project DB. Both went
/// through input code of their own and drifted (the agent's lacked the linked earlier release), so the two readings of
/// one project are compared here, the agent's through its own scan.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task GuiCheck_AndTheAgentsCheck_FindTheSameResults()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync(Header().Concat(Group(
                Record("WEAP", 0x800, Sub("FULL", Z("The One's Sword"))),
                Record("WEAP", 0x801, Sub("FULL", Z("Steel Sword"))),
                Record("WEAP", 0x802, Sub("FULL", Z("Iron Dagger"))),
                Record("WEAP", 0x803, Sub("FULL", Z("Glass Bow"))),
                Record("WEAP", 0x804, Sub("FULL", Z("Steel Shield"))),
                Record("WEAP", 0x805, Sub("FULL", Z("Elven Mace"))))).ToArray());
            var db = fixture.State.Db!;
            var rows = fixture.Vm.Entries.ToList();
            // Kept by the earlier release too: not left over. A bracket left open. English left over. Marked 원문 오류
            // 수정. The forced term missing. A TM fallback note the series TM does not explain.
            await db.UpdateStringTranslationAsync(rows[0].Id, "The One의 검", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[1].Id, "강철 (검", StringEntryStatus.Edited, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[2].Id, "Iron 단검", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[3].Id, "Ebony 활", StringEntryStatus.Edited, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[4].Id, "철 방패", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[5].Id, "엘프 철퇴", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpsertStringNoteAsync(rows[3].Id, TranslationConstants.SourceFixNoteKind, "원문 오타", CancellationToken.None);
            await db.UpsertStringNoteAsync(rows[5].Id, "tm_fallback", "TM 실패", CancellationToken.None);
            await db.UpsertGlossaryAsync(new GlossaryUpsertRequest(null, "Steel", "강철", true, 10,
                GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null), CancellationToken.None);
            await db.ReplacePreviousTranslationsAsync("Old.esp", new Dictionary<string, string> { ["WEAP/00000800/0/FULL/0"] = "The One의 검" },
                CancellationToken.None);
            var global = await fixture.GlobalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
            await global!.BulkUpsertTranslationMemoryAsync("english", "korean",
                new List<(string, string)> { ("Elven Mace", "엘ven 철퇴"), ("Iron Dagger", "철제 단검") }, CancellationToken.None);
            fixture.Ui.OpenPath = fixture.State.PluginDocument!.Info.InputPath;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);
            Assert.Equal("The One의 검", fixture.Vm.Entries[0].PreviousTranslation);
            Assert.True(fixture.Vm.Entries[3].IsSourceFix);

            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            var gui = fixture.Vm.LqaIssues.Select(i => (i.Id, i.Code, i.Message)).OrderBy(i => i.Id).ThenBy(i => i.Code).ToList();
            var (issues, _) = await AgentCli.ScanProjectAsync(fixture.State.Db!, fixture.GlobalDb, CancellationToken.None);
            var agent = issues.Select(i => (i.Id, i.Code, i.Message)).OrderBy(i => i.Id).ThenBy(i => i.Code).ToList();

            Assert.Equal(agent, gui);
            Assert.Contains(gui, i => i.Code == "bracket_mismatch");
            Assert.Contains(gui, i => i.Code == "english_residue" && i.Id == rows[2].Id);
            Assert.DoesNotContain(gui, i => i.Code == "english_residue" && (i.Id == rows[0].Id || i.Id == rows[3].Id));
            Assert.Contains(gui, i => i.Id == rows[4].Id);
            Assert.Contains(gui, i => i.Code == "tm_fallback" && i.Id == rows[5].Id);
        });
}
