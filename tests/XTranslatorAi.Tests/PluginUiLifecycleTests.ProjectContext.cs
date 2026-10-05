namespace XTranslatorAi.Tests;

/// <summary>
/// "문맥 생성" read every row of the project and the series TM on the window's thread (SQLite's async calls finish
/// synchronously), so the window froze for seconds on a large project before the request was even sent.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task GeneratingTheProjectContext_ScansOffTheWindowThread()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "{\"context\":\"검술 모드\"}" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var windowThread = Environment.CurrentManagedThreadId;
            int? scanThread = null;
            fixture.Vm.OnProjectContextScanForTests = () => scanThread = Environment.CurrentManagedThreadId;

            await fixture.Vm.GenerateProjectContextCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(scanThread);
            Assert.NotEqual(windowThread, scanThread);
            Assert.Equal("검술 모드", fixture.Vm.ProjectContextPreview);
        });
}
