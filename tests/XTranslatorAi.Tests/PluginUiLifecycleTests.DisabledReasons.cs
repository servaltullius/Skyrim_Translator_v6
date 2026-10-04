namespace XTranslatorAi.Tests;

/// <summary>Disabled save buttons and the read-only editor now say why.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task ExportTooltips_NameTheProjectKindThatFits()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await LoadXmlWorkspaceAsync(fixture, "Iron Sword");

            Assert.Contains("XML 프로젝트", fixture.Vm.ExportPluginToolTip);
            Assert.Equal("번역한 xTranslator XML을 내보냅니다.", fixture.Vm.ExportXmlToolTip);
            Assert.Contains("Ctrl+S", fixture.Vm.DestEditorToolTip);
        });
}
