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
}
