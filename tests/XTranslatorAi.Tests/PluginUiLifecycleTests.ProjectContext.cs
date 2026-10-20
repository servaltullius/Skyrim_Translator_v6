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

    /// <summary>
    /// Legacy of the Dragonborn's context failed with E310: the model's default thinking (2,756 tokens in one run)
    /// counts toward the output limit of 4,096. The request now sets translation's thinking level and a larger limit,
    /// and an answer cut off anyway is asked again with twice the room instead of failing.
    /// </summary>
    [Fact]
    public Task ProjectContextCutOffByTheOutputLimit_IsAskedAgainWithMoreRoom()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "{\"context\":\"박물관 모드\"}", TruncatedGenerations = 1 };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();

            await fixture.Vm.GenerateProjectContextCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("박물관 모드", fixture.Vm.ProjectContextPreview);
            var generations = handler.Requests.Where(r => r.Path.EndsWith(":generateContent", StringComparison.Ordinal)).Select(r => r.Body).ToList();
            Assert.Equal(2, generations.Count);
            Assert.Contains("\"maxOutputTokens\":8192", generations[0]);
            Assert.Contains("\"thinkingConfig\"", generations[0]);
            Assert.Contains("\"maxOutputTokens\":16384", generations[1]);
        });
}
