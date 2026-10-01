using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Tests;

// Damage written by builds before the 2026-10 post-processing fixes; examples are real outputs
// from the 2026-09-30 quality evaluation.
public sealed class LegacyPostEditDamageRuleTests
{
    private static async Task<IReadOnlyList<LqaIssue>> ScanAsync(string dest, string target = "korean")
    {
        var entry = new LqaScanEntry(1, 0, null, "BOOK:DESC", StringEntryStatus.Done, "Source text.", dest);
        var issues = await LqaScanner.ScanAsync(new[] { entry }, target, Array.Empty<GlossaryEntry>());
        return issues.Where(issue => issue.Code == "legacy_postedit_damage").ToList();
    }

    [Theory]
    [InlineData("그러나 무언이 이상했다.")]
    [InlineData("언젠이 꼭 그곳을 탐험하고 싶다.")]
    [InlineData("내가 이 무덤의 일부인이?")]
    [InlineData("참으로 기발하지 않은이?")]
    [InlineData("그래서 그대가가 책을 구매한 것이다.")]
    [InlineData("그를 찾을 만큼 가까가 다가가지 못했다.")]
    [InlineData("기꺼가 도전할 수 있어야 한다.")]
    [InlineData("추가적인 침략을 성공적으로 막아냈다.던머노드")]
    public async Task FindsDamageFromOlderBuilds(string dest)
    {
        var issue = Assert.Single(await ScanAsync(dest));
        Assert.Equal("Warn", issue.Severity);
    }

    [Theory]
    [InlineData("다른 누군가가 이 글을 읽고 있다면")]
    [InlineData("무언가 이상했다. 언젠가 다시 오겠다.")]
    [InlineData("내가 이 무덤의 일부인가?")]
    [InlineData("기꺼이 도전하고, 가까이 다가갔다.")]
    [InlineData("\"좋아.\"라고 그가 말했다.")]
    [InlineData("어린이가 울었다.")]
    public async Task LeavesCorrectTextAlone(string dest) => Assert.Empty(await ScanAsync(dest));

    [Fact]
    public async Task OnlyAppliesToKoreanTargets() => Assert.Empty(await ScanAsync("무언이", "english"));
}
