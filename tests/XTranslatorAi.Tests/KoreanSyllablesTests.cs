using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class KoreanSyllablesTests
{
    [Theory]
    [InlineData('검', true)]
    [InlineData('카', false)]
    [InlineData('룰', true)]
    [InlineData('A', false)]
    [InlineData('ㄱ', false)]
    public void HasFinalConsonant(char c, bool expected) => Assert.Equal(expected, KoreanSyllables.HasFinalConsonant(c));

    [Theory]
    [InlineData('룰', true)]
    [InlineData('럭', false)]
    [InlineData('루', false)]
    public void HasFinalRieul(char c, bool expected) => Assert.Equal(expected, KoreanSyllables.HasFinalRieul(c));

    [Theory]
    [InlineData("013678", true)]
    [InlineData("2459", false)]
    public void DigitReadings(string digits, bool expected)
    {
        foreach (var digit in digits) Assert.Equal(expected, KoreanSyllables.DigitHasFinalConsonant(digit));
    }

    [Theory]
    [InlineData('a', true)]
    [InlineData('Y', true)]
    [InlineData('m', false)]
    public void IsLatinVowel(char c, bool expected) => Assert.Equal(expected, KoreanSyllables.IsLatinVowel(c));
}
