using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Patterns found by comparing machine translations with reviewed rows (Elden Rim, Serana):
/// "Fortify X" names came out as "강화 X" in 20 of 50 rows, "Hey Nemiko!" lost its comma in 36 of 40 rows,
/// and unrequested Hanja glosses ("범인(凡人)") appeared in 34 rows.
/// </summary>
public class KoreanSourceAwareFixesTests
{
    [Theory]
    [InlineData("Fortify Secret Book", "강화 비급서", "비급서 강화")]
    [InlineData("Fortify Counter", "강화 반격", "반격 강화")]
    [InlineData("Fortify Mystic 3", "강화 신비 3", "신비 강화 3")]
    [InlineData("Fortify Sword Broken Range Drag", "강화 부러진 검 범위 끌어당기기", "부러진 검 범위 끌어당기기 강화")]
    public void FortifyName_PutsTheVerbLast(string source, string dest, string expected)
        => Assert.Equal(expected, KoreanSourceAwareFixes.Apply("korean", source, dest));

    [Theory]
    [InlineData("Fortify Health", "체력 강화")]                                   // already right
    [InlineData("Fortify Health by 10 points.", "강화 체력을 10포인트 올립니다.")]   // a sentence, not a name
    [InlineData("Fortified Wall", "강화 성벽")]                                  // not the Fortify effect
    [InlineData("Fortify Health", "강화")]                                       // nothing to move
    public void FortifyName_LeavesOtherTextAlone(string source, string dest)
        => Assert.Equal(dest, KoreanSourceAwareFixes.Apply("korean", source, dest));

    [Theory]
    [InlineData("Hey Nemiko!", "안녕 네미코!", "안녕, 네미코!")]
    [InlineData("Hi Drelorea.", "안녕 드렐로레아.", "안녕, 드렐로레아.")]
    [InlineData("Hey Sarah!", "이봐 사라!", "이봐, 사라!")]
    public void GreetingBeforeName_GetsAComma(string source, string dest, string expected)
        => Assert.Equal(expected, KoreanSourceAwareFixes.Apply("korean", source, dest));

    [Theory]
    [InlineData("Hey, look over there!", "저기 봐!")]
    [InlineData("Hey Nemiko!", "안녕, 네미코!")]
    [InlineData("Over there!", "저기 있다!")]
    [InlineData("Hey Nemiko, wait!", "안녕 네미코 기다려!")]
    public void GreetingComma_OnlyForAGreetingAndOneName(string source, string dest)
        => Assert.Equal(dest, KoreanSourceAwareFixes.Apply("korean", source, dest));

    [Theory]
    [InlineData("A skill beyond the reach of most mortals.", "범인(凡人)의 경지를 넘어선 기술.", "범인의 경지를 넘어선 기술.")]
    [InlineData("The flag flew at half-mast.", "깃발은 반기(半旗)로 걸려 있었다.", "깃발은 반기로 걸려 있었다.")]
    public void HanjaGloss_NotInSource_IsRemoved(string source, string dest, string expected)
        => Assert.Equal(expected, KoreanSourceAwareFixes.Apply("korean", source, dest));

    [Theory]
    [InlineData("体力恢复速度减半。", "체력(体力) 회복 속도가 절반으로 줄어듭니다.")] // the source is Chinese
    [InlineData("A note (see 凡人).", "메모 (凡人 참조).")]                               // not a gloss after Hangul
    public void HanjaGloss_KeptWhenSourceHasIdeographsOrNotAGloss(string source, string dest)
        => Assert.Equal(dest, KoreanSourceAwareFixes.Apply("korean", source, dest));

    [Fact]
    public void OtherTargetLanguages_AreUntouched()
        => Assert.Equal("강화 비급서", KoreanSourceAwareFixes.Apply("japanese", "Fortify Secret Book", "강화 비급서"));
}

public class KoreanSourceAwareFixesPostEditTests
{
    [Fact]
    public void ReappliedPostEdits_IncludeTheSourceAwareFixes()
        => Assert.Equal("반격 강화", TranslationPostEdits.Apply("korean", "Fortify Counter", "강화 반격", enableTemplateFixer: true));
}
