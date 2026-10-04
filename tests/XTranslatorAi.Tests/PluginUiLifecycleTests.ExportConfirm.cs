using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>Saving with rows in error or pending used to finish silently; it now asks first.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task ExportingXml_WithUnfinishedRows_AsksAndCanBeDeclined()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword");
            rows[0].Status = StringEntryStatus.Error;
            var exportPath = Path.Combine(fixture.Root, "out.xml");
            fixture.Ui.SavePath = exportPath;

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.ExportXmlCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("저장 확인", fixture.Ui.Titles);
            Assert.False(File.Exists(exportPath));
            Assert.StartsWith("저장하지 않았습니다", fixture.Vm.StatusMessage);
        });
}
