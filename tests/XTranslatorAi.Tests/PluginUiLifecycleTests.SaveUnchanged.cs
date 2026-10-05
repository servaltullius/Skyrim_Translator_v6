using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// Ctrl+S or Ctrl+Enter on a row nobody typed into saved it as a manual edit: a pending row became "직접 수정" with
/// its empty or English text, was skipped by every later run and went into the plugin as written.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task SavingARowNobodyChanged_LeavesItAsItWas()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword");
            var db = fixture.State.Db!;
            await db.UpdateStringTranslationAsync(rows[1].Id, "강철 검", StringEntryStatus.Done, null, CancellationToken.None);
            (rows[1].DestText, rows[1].Status) = ("강철 검", StringEntryStatus.Done);
            rows[1].MarkDestTextSaved("강철 검");

            foreach (var row in rows)
            {
                fixture.Vm.SelectedEntry = row;
                await fixture.Vm.SaveSelectedDestCommand.ExecuteAsync(null);
            }

            var saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal(StringEntryStatus.Pending, saved.Single(r => r.Id == rows[0].Id).Status);
            Assert.Equal(StringEntryStatus.Done, saved.Single(r => r.Id == rows[1].Id).Status);
        });
}
