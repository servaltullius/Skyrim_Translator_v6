namespace XTranslatorAi.Tests;

/// <summary>
/// The prompt used the project context box as typed, but the edit was only saved by its button: closing or opening
/// another file dropped it without asking, and the next run used the old context.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task AnEditedProjectContext_IsSavedWhenTheProjectCloses()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");
            var db = fixture.State.Db!;
            await db.UpsertProjectContextAsync("예전 문맥", CancellationToken.None);
            fixture.Vm.ProjectContextPreview = "페리스는 반말을 쓴다.";

            Assert.True(await fixture.Vm.TryCloseWorkspaceAsync());

            await using var reopened = await XTranslatorAi.Core.Data.ProjectDb.OpenOrCreateAsync(fixture.DbPath, CancellationToken.None);
            Assert.Equal("페리스는 반말을 쓴다.", (await reopened.TryGetProjectContextAsync(CancellationToken.None))?.ContextText);
        });
}
