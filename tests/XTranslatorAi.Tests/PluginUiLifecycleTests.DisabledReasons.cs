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

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task StatusBarTooltip_KeepsRecentMessages_WithoutProgressUpdates()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            fixture.Vm.StatusMessage = "시리즈 TM을 가져오지 못했습니다(인코딩).";
            fixture.Vm.StatusMessage = "품질 검사 중... 40%";
            fixture.Vm.StatusMessage = "플러그인 읽기 완료";

            var history = fixture.Vm.StatusHistoryText;
            Assert.Contains("시리즈 TM을 가져오지 못했습니다", history);
            Assert.Contains("플러그인 읽기 완료", history);
            Assert.DoesNotContain("40%", history);
            Assert.True(history.IndexOf("플러그인 읽기 완료", StringComparison.Ordinal) < history.IndexOf("시리즈 TM", StringComparison.Ordinal));
        });
}
