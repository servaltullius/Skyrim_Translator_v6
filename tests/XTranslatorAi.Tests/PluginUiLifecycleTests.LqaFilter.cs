using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.Tests;

/// <summary>
/// Information-only results (another speaker's tone, official names, TM fallbacks) can outnumber the warnings
/// many times over (442 on the pre-review Serana project), and the list had only a text search.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task HidingInformation_LeavesErrorsAndWarnings_AndCountsWhatIsShown()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            fixture.Vm.LqaIssues.ReplaceAll(new[]
            {
                new LqaIssueViewModel(1, 1, null, "INFO:NAM1", "Warn", "tone_inconsistent", "m", "s", "d"),
                new LqaIssueViewModel(2, 2, null, "INFO:NAM1", "Info", "tone_differs_from_plugin", "m", "s", "d"),
                new LqaIssueViewModel(3, 3, null, "BOOK:DESC", "Error", "token_mismatch", "m", "s", "d"),
            });
            fixture.Vm.LqaIssuesView.Refresh();
            Assert.Equal("전체 3건", fixture.Vm.LqaTab.LqaVisibleSummary);

            fixture.Vm.LqaTab.LqaHideInfo = true;

            Assert.Equal(new long[] { 1, 3 }, fixture.Vm.LqaIssuesView.Cast<LqaIssueViewModel>().Select(i => i.Id));
            Assert.Equal("표시 2 / 전체 3건", fixture.Vm.LqaTab.LqaVisibleSummary);
        });
}
