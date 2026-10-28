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

    // The model left out the last line with its line break; the repair put the break back and the empty slot passed as
    // joined title lines, so the description was saved without "Costs 50 magicka and staggers the caster."
    [Fact]
    public void RepairThatLeavesASentenceSlotEmpty_IsRefused()
    {
        const string input = "Fire Storm__XT_PH_0000__A blast of fire hits everyone nearby.__XT_PH_0001__Costs 50 magicka and staggers the caster.";
        const string output = "화염 폭풍__XT_PH_0000__주변의 모두에게 피해를 주는 화염 폭발.";

        var ex = Assert.Throws<InvalidOperationException>(() => TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "id=1"));
        Assert.Contains("dropped a line", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairThatJoinsATitleFragment_IsAllowed()
    {
        // The model dropped the break of "A Short History / of Morrowind"; a title fragment may still be joined.
        const string input = "A Short History__XT_PH_0000__of Morrowind__XT_PH_0001__by an Imperial scholar";

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, "모로윈드 약사__XT_PH_0001__제국 학자 지음", "id=1");

        Assert.Contains("__XT_PH_0000__", repaired, StringComparison.Ordinal);
        Assert.Contains("모로윈드 약사", repaired, StringComparison.Ordinal);
    }

    // The repair puts a lost line break back at the end, so a line dropped in the middle left the last slot, a short
    // byline, empty: judged by the slot, the dropped sentence passed.
    [Fact]
    public void RepairOfALineDroppedInTheMiddle_IsRefused()
    {
        const string input = "The Book of Dawn and Dusk__XT_PH_0000__An account of the ancient Akaviri and their wars.__XT_PH_0001__by Anonymous";

        var ex = Assert.Throws<InvalidOperationException>(
            () => TokenSanitizer.EnsureTokensPreservedOrRepair(input, "새벽과 황혼의 서__XT_PH_0000__작자 미상", "id=1"));
        Assert.Contains("dropped a line", ex.Message, StringComparison.Ordinal);
    }

    // Plain __XT_PH_ tokens are formatting tags too: a term put back before a tag left the English before the tag an
    // empty slot in Korean word order, and a complete translation was refused.
    [Fact]
    public void RepairOfATermBeforeATag_IsAllowed()
    {
        const string input = "Speak to the court wizard in Dragonsreach about __XT_PH_0000____XT_TERM_0000____XT_PH_0001__.";
        const string output = "__XT_PH_0000__블리크 폴스 바로우__XT_PH_0001__에 대해 드래곤스리치의 궁정 마법사와 이야기하라.";
        var terms = new Dictionary<string, string>(StringComparer.Ordinal) { ["__XT_TERM_0000__"] = "블리크 폴스 바로우" };

        var repaired = TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "id=1", terms);

        Assert.StartsWith("__XT_PH_0000____XT_TERM_0000____XT_PH_0001__", repaired, StringComparison.Ordinal);
    }

    // A name or value token follows Korean word order: cut at it, the long English before the name had an empty Korean
    // segment and a complete translation was refused as an omission, on every retry.
    [Fact]
    public void LongSentence_WithItsNameMovedToTheFront_IsNotAnOmission()
    {
        var input = Words(300) + " __XT_TERM_0000__.";
        var output = "__XT_TERM_0000__, " + Hangul(150) + ".";

        TokenValidator.ValidateNotTruncatedOrOmitted(input, output, "id=1");
        // A long line that really lost most of its text is still refused, its tokens' letters not counted as text.
        Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateNotTruncatedOrOmitted(input, "__XT_TERM_0000__, 짧음.", "id=1"));
        var names = string.Join(" ", Enumerable.Range(0, 6).Select(i => $"__XT_TERM_{i:0000}__"));
        Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateNotTruncatedOrOmitted(Words(300) + " " + names + ".", names + " 짧음.", "id=1"));
    }
}
