using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>The quality check runs its rules away from the UI thread and publishes the result on it.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task QualityCheck_RunsTheRulesOffTheUiThread_AndPublishesTheResult()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            row.DestText = "철검 (손잡이";
            row.Status = StringEntryStatus.Done;
            var ui = (StaContext)SynchronizationContext.Current!;
            var uiThread = Environment.CurrentManagedThreadId;
            var progressFromWorker = 0;
            // Progress<int> posts each percentage to the UI context; a scan on the UI thread reported it directly.
            ui.Posted = state =>
            {
                if (state is int && Environment.CurrentManagedThreadId != uiThread)
                {
                    Interlocked.Increment(ref progressFromWorker);
                }
            };

            await fixture.Vm.ScanLqaCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(Volatile.Read(ref progressFromWorker) > 0, "The quality check ran its rules on the UI thread.");
            var issue = Assert.Single(fixture.Vm.LqaIssues, i => i.Code == "bracket_mismatch");
            Assert.Equal(row.Id, issue.Id);
            Assert.False(fixture.Vm.IsLqaScanning);
            Assert.StartsWith("품질 검사: ", fixture.Vm.StatusMessage);
        });
}
