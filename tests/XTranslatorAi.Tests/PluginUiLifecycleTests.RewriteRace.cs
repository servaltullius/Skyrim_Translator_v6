using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>
/// The rewrite tools ("태그 교정", "후처리 재적용") compute their fixes off the UI thread from a snapshot and then
/// wrote them over the rows, so a row the user changed meanwhile lost that change.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task RewriteTool_SkipsARowChangedWhileItRuns()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Iron Shield");
            var db = fixture.State.Db!;
            foreach (var (row, text) in new[] { (rows[0], "철 검"), (rows[1], "철 방패") })
            {
                await db.UpdateStringTranslationAsync(row.Id, text, StringEntryStatus.Done, null, CancellationToken.None);
                (row.DestText, row.Status) = (text, StringEntryStatus.Done);
            }

            var started = new TaskCompletionSource();
            using var release = new ManualResetEventSlim();
            var rewrite = fixture.Vm.RewriteFinishedRowsAsync(db, "후처리 재적용", "진행", "완료", row =>
            {
                started.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
                return row.Dest + "!";
            });
            await started.Task;
            rows[0].DestText = "강철 검";
            release.Set();
            await rewrite;

            Assert.Equal("강철 검", rows[0].DestText);
            Assert.Equal("철 방패!", rows[1].DestText);
            var saved = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.Equal("철 검", saved.Single(r => r.Id == rows[0].Id).DestText);
            Assert.Equal("철 방패!", saved.Single(r => r.Id == rows[1].Id).DestText);
        });
}
