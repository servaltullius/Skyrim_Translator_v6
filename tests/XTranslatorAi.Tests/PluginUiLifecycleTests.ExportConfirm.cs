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
            // An XML keeps the translation field, so an untranslated row goes out empty, not as its source.
            Assert.Contains(fixture.Ui.Messages, m => m.Contains("번역란이 지금 그대로") && !m.Contains("원문"));
            Assert.False(File.Exists(exportPath));
            Assert.StartsWith("저장하지 않았습니다", fixture.Vm.StatusMessage);
        });

    [Fact]
    public Task ExportingPlugin_WithUnfinishedRows_SaysTheSourceStays()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            Assert.Equal(StringEntryStatus.Pending, Assert.Single(fixture.Vm.Entries).Status);
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains(fixture.Ui.Messages, m => m.Contains("원문이 그대로 남습니다"));
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
        });
}
