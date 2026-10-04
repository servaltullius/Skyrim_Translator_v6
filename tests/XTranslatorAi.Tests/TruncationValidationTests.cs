using System;
using System.Linq;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The last check before a translation is saved: an empty or cut-off answer, or lines pushed past the layout
/// tokens, must be rejected (and retried), while ordinary short or reordered answers must pass.
/// </summary>
public class TruncationValidationTests
{
    private static string Words(int letters) => string.Join(" ", Enumerable.Repeat("abcde", letters / 5));

    private static string Hangul(int letters) => string.Join(" ", Enumerable.Repeat("가나다라마", letters / 5));

    [Fact]
    public void EmptyOutput_ForNonEmptyInput_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateNotTruncatedOrOmitted("Hello there.", " ", "id=1"));
        Assert.Contains("empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortSegment_IsNeverRejected()
        => TokenValidator.ValidateNotTruncatedOrOmitted(Words(235), "짧음", "id=1");

    [Theory]
    [InlineData(300, 55, true)]   // required max(60, 48) = 60
    [InlineData(300, 60, false)]
    [InlineData(1000, 195, true)] // required max(120, 200) = 200
    [InlineData(1000, 200, false)]
    public void LongSegment_BelowMinimumRatio_Throws(int inputLetters, int outputLetters, bool throws)
    {
        void Validate() => TokenValidator.ValidateNotTruncatedOrOmitted(Words(inputLetters), Hangul(outputLetters), "id=1");

        if (throws)
        {
            var ex = Assert.Throws<InvalidOperationException>(Validate);
            Assert.Contains("omit", ex.Message, StringComparison.Ordinal);
        }
        else
        {
            Validate();
        }
    }

    [Fact]
    public void ThreeShiftedLayoutSlots_Throw()
    {
        const string input = "Line one__XT_PH_0000____XT_PH_0001____XT_PH_0002____XT_PH_0003__Line two";
        const string output = "첫 줄__XT_PH_0000__밀림__XT_PH_0001__밀림__XT_PH_0002__밀림__XT_PH_0003__둘째 줄";

        var ex = Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateNotTruncatedOrOmitted(input, output, "id=1"));
        Assert.Contains("shifted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoShiftedLayoutSlots_AreAllowed()
    {
        const string input = "Line one__XT_PH_0000____XT_PH_0001____XT_PH_0002____XT_PH_0003__Line two";
        const string output = "첫 줄__XT_PH_0000__밀림__XT_PH_0001__밀림__XT_PH_0002____XT_PH_0003__둘째 줄";

        TokenValidator.ValidateNotTruncatedOrOmitted(input, output, "id=1");
    }

    [Fact]
    public void JoinedTitleLines_AreAllowed()
    {
        // "A Short History / of Morrowind" becomes one Korean line; the second slot empties out.
        const string input = "A Short History__XT_PH_0000__of Morrowind";
        const string output = "모로윈드 약사__XT_PH_0000__";

        TokenValidator.ValidateNotTruncatedOrOmitted(input, output, "id=1");
    }
}
