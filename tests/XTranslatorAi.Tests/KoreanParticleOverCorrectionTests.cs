using System.Collections.Generic;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

// Sentences the particle fixer used to corrupt in the 2026-09-30 quality evaluation
// (docs/analysis/2026-09-30-quality-eval-v2.md). A word that merely ends in a
// particle-like syllable must stay as the model wrote it.
public class KoreanParticleOverCorrectionTests
{
    [Theory]
    [InlineData("무언가 이상했다.")]
    [InlineData("언젠가 꼭 그곳을 탐험하고 싶다.")]
    [InlineData("어딘가 묘한 유머가 느껴진다.")]
    [InlineData("누군가 보고 있다는 사실을 깨달았다.")]
    [InlineData("그대가 이 책을 구매한 것이다.")]
    [InlineData("다른 누군가가 이 글을 읽고 있다면")]
    [InlineData("내가 이 무덤의 일부인가?")]
    [InlineData("참으로 기발하지 않은가.")]
    [InlineData("전문가 주문을 최대 2개까지 등록할 수 있습니다.")]
    [InlineData("탐험가 길드에 가입했다.")]
    [InlineData("기꺼이 도전할 수 있어야 한다.")]
    [InlineData("그를 찾을 만큼 가까이 다가가지 못했다.")]
    [InlineData("어린아이 둘이 뛰어놀았다.")]
    [InlineData("가장 우선해야 할 것은 살아남는 것이다.")]
    [InlineData("사람을 잡아먹는 괴물이다.")]
    [InlineData("신화 시대 은 드래곤마크")]
    [InlineData("시골마을 사람들은 친절하다.")]
    [InlineData("저녁노을 아래에서 쉬었다.")]
    [InlineData("어서 가!")]
    [InlineData("여기서 그만 가 봐.")]
    public void Fix_LeavesWordsEndingInParticleLikeSyllables(string text)
    {
        Assert.Equal(text, KoreanTranslationFixer.Fix("korean", text));
    }

    public static IEnumerable<object[]> TermParticleCases()
    {
        yield return new object[] { "화이트런", "와 싸웠다.", "화이트런과 싸웠다." };
        yield return new object[] { "매지카", "을 흡수합니다.", "매지카를 흡수합니다." };
        yield return new object[] { "지구력", "가 감소합니다.", "지구력이 감소합니다." };
        yield return new object[] { "해머펠", "는 남쪽에 있다.", "해머펠은 남쪽에 있다." };
        yield return new object[] { "세계의 목", "라 부른다.", "세계의 목이라 부른다." };
        yield return new object[] { "함성", "나 통솔의 힘", "함성이나 통솔의 힘" };
        yield return new object[] { "솔리튜드", "으로 향했다.", "솔리튜드로 향했다." };
        yield return new object[] { "마르카스", "으로부터 왔다.", "마르카스로부터 왔다." };
        yield return new object[] { "해머펠", "로 향했다.", "해머펠로 향했다." };
        yield return new object[] { "리프튼", "로 향했다.", "리프튼으로 향했다." };
        yield return new object[] { "Skyrim", "가 좋다.", "Skyrim이 좋다." };
        yield return new object[] { "야를", "과의 대화", "야를과의 대화" };
        // Both forms written after the token (1.9 evaluation: "히얄마치를(를)", "요새는(는)", "자리-라가(가)").
        yield return new object[] { "히얄마치", "을(를) 탈환하면", "히얄마치를 탈환하면" };
        yield return new object[] { "프로스트모스 요새", "은(는) 한때", "프로스트모스 요새는 한때" };
        yield return new object[] { "자리-라", "이(가) 제안했다.", "자리-라가 제안했다." };
        yield return new object[] { "리프튼", "(으)로 향했다.", "리프튼으로 향했다." };
        yield return new object[] { "화이트런", "과/와 싸웠다.", "화이트런과 싸웠다." };
        yield return new object[] { "세계의 목", "(이)라 부른다.", "세계의 목이라 부른다." };
        yield return new object[] { "노드", "는(은) 강하다.", "노드는 강하다." };
        // Not a particle: the syllable starts the next word.
        yield return new object[] { "달인", "과거를 회상했다.", "달인과거를 회상했다." };
    }

    [Theory]
    [MemberData(nameof(TermParticleCases))]
    public void ReplaceGlossaryTokens_FixesParticleRightAfterTerm_ForKorean(string term, string rest, string expected)
    {
        const string token = "__XT_TERM_G1_0000__";
        var replacements = new Dictionary<string, string> { [token] = term };
        Assert.Equal(expected, TranslationService.ReplaceGlossaryTokens(token + rest, replacements, fixKoreanParticles: true));
    }

    [Fact]
    public void ReplaceGlossaryTokens_LeavesParticles_WhenNotKorean()
    {
        const string token = "__XT_TERM_G1_0000__";
        var replacements = new Dictionary<string, string> { [token] = "화이트런" };
        Assert.Equal("화이트런와", TranslationService.ReplaceGlossaryTokens(token + "와", replacements));
    }
}
