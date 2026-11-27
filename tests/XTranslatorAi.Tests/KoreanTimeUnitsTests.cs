using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Tests;

public sealed class KoreanTimeUnitsTests
{
    // The template fixer and the answer check read a unit's 의 the same way: the 1.16.40 fix reached only the fixer.
    [Theory]
    [InlineData("의력", true)]
    [InlineData("의를 빼앗습니다", true)]
    [InlineData("의 1로", true)]
    [InlineData("의1", true)]
    [InlineData("의　２", true)]
    [InlineData("의 기간 동안", false)]
    [InlineData("의", false)]
    [InlineData("의.", false)]
    [InlineData("", false)]
    [InlineData("노", false)]
    public void OfMakesAWordOrFraction(string afterUnit, bool expected)
        => Assert.Equal(expected, KoreanTimeUnits.OfMakesAWordOrFraction(afterUnit));

    [Theory]
    [InlineData("초 동안", true, true)]
    [InlineData("초간", true, true)]
    [InlineData("일째 불탑니다.", true, true)]
    [InlineData("일의 기간 동안", true, true)]
    [InlineData("초과일 때", true, false)]
    [InlineData("년도 기록", true, false)]
    [InlineData("간격으로", true, false)]
    [InlineData("주의력을", true, false)]
    [InlineData("분의 1로", true, false)]
    [InlineData("초당 피해를", true, true)]
    [InlineData("초당 피해를", false, false)]
    [InlineData("주고 넘어뜨립니다.", true, false)]
    [InlineData("주는 불덩이", true, false)]
    [InlineData("주도로", true, false)]
    // A follower whose first syllable also begins a word is read in full.
    [InlineData("일부를 흡수합니다.", true, false)]
    [InlineData("일부터 시작합니다.", true, true)]
    [InlineData("일정 확률로", true, false)]
    [InlineData("일 정도", true, true)]
    [InlineData("일정도 걸립니다.", true, true)]
    [InlineData("초보 마법사", true, false)]
    [InlineData("초보다 길게", true, true)]
    [InlineData("초입에서", true, false)]
    [InlineData("초입니다.", true, true)]
    [InlineData("초동 조치", true, false)]
    [InlineData("초동안", true, true)]
    [InlineData("초마다", true, true)]
    [InlineData("분까지", true, true)]
    [InlineData("초짜리", true, true)]
    [InlineData("주로 화염 피해를", true, false)]
    [InlineData("주간", true, true)]
    [InlineData("초로 늘어납니다.", true, true)]
    [InlineData("분당 피해를", true, true)]
    [InlineData("분당 피해를", false, false)]
    [InlineData("일당 한 번", false, false)]
    // Endings the fixer read before it shared this check (any syllable after 초, 년 was a unit to it).
    [InlineData("초면 발동합니다.", true, true)]
    [InlineData("초인 경우", true, true)]
    [InlineData("초나 지속됩니다.", true, true)]
    [InlineData("초라도", true, true)]
    [InlineData("초밖에", true, true)]
    [InlineData("초미만", true, true)]
    [InlineData("초일 때", true, true)]
    [InlineData("초지만", true, true)]
    [InlineData("초뿐입니다.", true, true)]
    [InlineData("년차", true, true)]
    [InlineData("일차 방어선", true, false)]
    [InlineData("초라한", true, false)]
    // 주다 again, and an owner.
    [InlineData("주면 쓰러집니다.", true, false)]
    [InlineData("주나", true, false)]
    [InlineData("주지만", true, false)]
    [InlineData("주거나", true, false)]
    [InlineData("주라는", true, false)]
    [InlineData("주인에게", true, false)]
    [InlineData("주마다", true, true)]
    [InlineData("주 동안", true, true)]
    // 일 as 이다.
    [InlineData("일 때", true, false)]
    [InlineData("일 경우", true, false)]
    [InlineData("일 수 있습니다.", true, false)]
    [InlineData("일 동안", true, true)]
    [InlineData("일", true, true)]
    public void StartsWithTimeUnit_ReadsUnitsAndWordsConsistently(string text, bool allowPerUnit, bool expected)
        => Assert.Equal(expected, KoreanTimeUnits.StartsWithTimeUnit(text, allowPerUnit));
}
