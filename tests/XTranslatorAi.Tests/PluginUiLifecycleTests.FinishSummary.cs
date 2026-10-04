using System.Reflection;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>"번역을 마쳤습니다." alone hid failed rows; the finish message now counts errors and rows left.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task FinishedRunMessage_CountsErrorsAndRowsLeft()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword", "Elven Sword");
            var describe = typeof(XTranslatorAi.App.ViewModels.MainViewModel)
                .GetMethod("DescribeFinishedRun", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var row in rows) row.Status = StringEntryStatus.Done;
            Assert.Equal("번역을 마쳤습니다.", describe.Invoke(fixture.Vm, null));

            rows[0].Status = StringEntryStatus.Error;
            rows[1].Status = StringEntryStatus.Pending;
            var message = (string)describe.Invoke(fixture.Vm, null)!;
            Assert.StartsWith("번역을 마쳤습니다. 오류 1개, 남은 행 1개가 있습니다.", message);
            Assert.Contains("'오류'", message);
        });
}
