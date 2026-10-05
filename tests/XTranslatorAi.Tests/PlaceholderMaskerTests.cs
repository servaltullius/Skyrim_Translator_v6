using System;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class PlaceholderMaskerTests
{
    [Fact]
    public void MaskAndUnmask_RoundTrips()
    {
        var masker = new PlaceholderMasker();
        var input = "[pagebreak]\nWeapons and armor can be improved <mag>% better.\n%0f research points earned";

        var masked = masker.Mask(input);
        Assert.NotEqual(input, masked.Text);
        Assert.Contains("__XT_PH_", masked.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("%", masked.Text, StringComparison.Ordinal);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Unmask_Throws_WhenTokenMissing()
    {
        var masker = new PlaceholderMasker();
        var input = "Letter from <Alias=Enemy>";
        var masked = masker.Mask(input);

        var bad = masked.Text.Replace("__XT_PH_VAR_0000__", "MISSING", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => masker.Unmask(bad, masked.TokenToOriginal));
    }

    [Fact]
    public void Mask_IncludesLeadingPlusOrMinus_WithPlaceholderTag()
    {
        var masker = new PlaceholderMasker();
        var input = "+<mag> Speech for <dur> seconds. -<mag> Health.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_MAG_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_DUR_0001__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_MAG_0002__", masked.Text, StringComparison.Ordinal);

        Assert.Equal("+<mag>", masked.TokenToOriginal["__XT_PH_MAG_0000__"]);
        Assert.Equal("<dur>", masked.TokenToOriginal["__XT_PH_DUR_0001__"]);
        Assert.Equal("-<mag>", masked.TokenToOriginal["__XT_PH_MAG_0002__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_LabelsAngleBracketNumbers_AsNUM()
    {
        var masker = new PlaceholderMasker();
        var input = "Health by <15> points for <dur> seconds.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_NUM_", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_DUR_", masked.Text, StringComparison.Ordinal);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_LabelsSemanticPlaceholders_WithWhitespaceAndCaseVariations()
    {
        var masker = new PlaceholderMasker();
        var input = "+< mag > Speech for < Dur > seconds. -< MAG > Health by < 15 > points.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_MAG_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_DUR_0001__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_MAG_0002__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_NUM_0003__", masked.Text, StringComparison.Ordinal);

        Assert.Equal("+< mag >", masked.TokenToOriginal["__XT_PH_MAG_0000__"]);
        Assert.Equal("< Dur >", masked.TokenToOriginal["__XT_PH_DUR_0001__"]);
        Assert.Equal("-< MAG >", masked.TokenToOriginal["__XT_PH_MAG_0002__"]);
        Assert.Equal("< 15 >", masked.TokenToOriginal["__XT_PH_NUM_0003__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_LabelsAngleBracketNumbers_FollowedBySeconds_AsDUR()
    {
        var masker = new PlaceholderMasker();
        var input = "Heals <2> points per second for <120> seconds.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_NUM_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_DUR_0001__", masked.Text, StringComparison.Ordinal);

        Assert.Equal("<2>", masked.TokenToOriginal["__XT_PH_NUM_0000__"]);
        Assert.Equal("<120>", masked.TokenToOriginal["__XT_PH_DUR_0001__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void MaskAndUnmask_PreservesCrLfNewlines()
    {
        var masker = new PlaceholderMasker();
        var input = "Line1\r\nLine2\r\n+<mag> Health.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_", masked.Text, StringComparison.Ordinal);
        Assert.Contains("\r\n", masked.TokenToOriginal.Values, StringComparer.Ordinal);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_LabelsNumericPercent_WithWhitespace_AsNUM()
    {
        var masker = new PlaceholderMasker();
        var input = "80 %";

        var masked = masker.Mask(input);
        Assert.Equal("__XT_PH_NUM_0000__", masked.Text);
        Assert.Equal("80 %", masked.TokenToOriginal["__XT_PH_NUM_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_LabelsAngleBracketPercent_WithWhitespace_AsNUM()
    {
        var masker = new PlaceholderMasker();
        var input = "<10> %";

        var masked = masker.Mask(input);
        Assert.Equal("__XT_PH_NUM_0000__", masked.Text);
        Assert.Equal("<10> %", masked.TokenToOriginal["__XT_PH_NUM_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_LabelsAngleBracketPercentInsideTag_AsNUM()
    {
        var masker = new PlaceholderMasker();
        var input = "You are <100%> weaker to shock for <30> seconds.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_NUM_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_DUR_0001__", masked.Text, StringComparison.Ordinal);

        Assert.Equal("<100%>", masked.TokenToOriginal["__XT_PH_NUM_0000__"]);
        Assert.Equal("<30>", masked.TokenToOriginal["__XT_PH_DUR_0001__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksPercentWrappedIdentifiers_AsSinglePlaceholder()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello %PLAYERNAME%.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_VAR_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("%PLAYERNAME%", masked.TokenToOriginal["__XT_PH_VAR_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksPrintfPositionalSpecifiers_AsSinglePlaceholder()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello %1$s.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_VAR_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("%1$s", masked.TokenToOriginal["__XT_PH_VAR_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksDollarWrappedIdentifiers()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello $PLAYERNAME$.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_VAR_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("$PLAYERNAME$", masked.TokenToOriginal["__XT_PH_VAR_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksBraceWrappedIdentifiers()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello {PLAYERNAME}.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_VAR_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("{PLAYERNAME}", masked.TokenToOriginal["__XT_PH_VAR_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksDoubleBraceWrappedIdentifiers()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello {{PLAYERNAME}}.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_VAR_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("{{PLAYERNAME}}", masked.TokenToOriginal["__XT_PH_VAR_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    // Serana Dialogue Add-On: "<Take a deep breath>" is a stage direction shown in the subtitle, so it is translated.
    [Fact]
    public void Mask_LeavesStageDirectionsAsText_ButKeepsTags()
    {
        var masked = new PlaceholderMasker().Mask("<Take a deep breath> Far. <Alias=Player>, deal <mag> damage.<br><font face='$HandwrittenFont'>x</font>");

        Assert.StartsWith("<Take a deep breath> Far.", masked.Text);
        Assert.DoesNotContain("<Alias=Player>", masked.Text);
        Assert.DoesNotContain("<mag>", masked.Text);
        Assert.DoesNotContain("<br>", masked.Text);
        Assert.DoesNotContain("<font face", masked.Text);
        // Legacy of the Dragonborn writes its page break as "<page break>".
        Assert.DoesNotContain("<page break>", new PlaceholderMasker().Mask("One.<page break>Two.").Text);
    }

    // MEI's player options: "< Tell Senna you're visiting with a friend. >". An apostrophe, a slash between words or
    // a digit made the whole option a tag, so it was never translated and the quality check did not see it either.
    [Theory]
    [InlineData("< Tell Senna you're visiting with a friend. >")]
    [InlineData("< Haelga / Svana, can I ask you something? >")]
    [InlineData("< Ask Haelga for 3P with Maven (ask Maven for a 3P first) >")]
    [InlineData("< Randomized end / walkaway topic for Elenwen's reply. >")]
    public void Mask_LeavesSentencesInAngleBracketsAsText(string option)
        => Assert.Equal(option, new PlaceholderMasker().Mask(option).Text);

    [Theory]
    [InlineData("<br />")]
    [InlineData("<p align='center'>")]
    [InlineData("<font face='$HandwrittenFont' size='15'>")]
    [InlineData("<img src='img://Textures/Map.dds' width='512' height='256'>")]
    [InlineData("</font>")]
    public void Mask_StillKeepsTagsWithQuotesAndSlashes(string tag)
        => Assert.DoesNotContain("<", new PlaceholderMasker().Mask("Text " + tag + " more.").Text);

    [Fact]
    public void QualityCheck_ReadsAStageDirectionAsText()
    {
        Assert.False(XTranslatorAi.Core.Text.LqaScanner.HasTokenMismatch("<Relieved smile> Thank you, Serana.", "<안도의 미소> 고마워, 세라나."));
        Assert.Equal("Relieved", XTranslatorAi.Core.Text.LqaScanner.FindEnglishResidue("<Relieved smile> 고마워, 세라나.", "<Relieved smile> Thank you, Serana."));
    }

    // "<Clears throat> Fine." keeps the stage direction as text, but the model's one-word "<헛기침>" looked like
    // a tag missing from the source, so the sanitizer deleted it and the final check rejected the row.
    [Fact]
    public void OneWordKoreanStageDirection_IsKeptAsText()
    {
        const string source = "<Clears throat> Fine.";
        const string translated = "<헛기침> 좋아.";

        Assert.Equal(translated, XTranslatorAi.Core.Translation.TokenSanitizer.SanitizeModelTranslationText(translated, source));
        Assert.Equal(translated, XTranslatorAi.Core.Translation.TokenSanitizer.EnsureTokensPreservedOrRepair(source, translated, "test"));
        XTranslatorAi.Core.Translation.TokenValidator.ValidateFinalTextIntegrity(source, translated, "test");
        Assert.False(LqaScanner.HasTokenMismatch(source, translated));
        Assert.Equal("<헛기침> 좋아.", new PlaceholderMasker().Mask(translated).Text);
    }

    // Legacy of the Dragonborn writes its page breaks as "[pagebreak]", "[page break]", "<page break>" and "<pagebreak>".
    // "<page break>" was a value token free to move anywhere, and "[page break]" was not protected at all.
    [Theory]
    [InlineData("[pagebreak]")]
    [InlineData("[page break]")]
    [InlineData("[Page Break]")]
    [InlineData("<page break>")]
    [InlineData("<pagebreak>")]
    public void Mask_TreatsEveryPageBreakFormAsLayout(string pageBreak)
    {
        var masked = new PlaceholderMasker().Mask($"Fate of the Snow Elves{pageBreak}Some things about the chronology.");

        Assert.Equal("Fate of the Snow Elves__XT_PH_0000__Some things about the chronology.", masked.Text);
        Assert.Equal(pageBreak, masked.TokenToOriginal["__XT_PH_0000__"]);
    }

    [Fact]
    public void PageBreakMovedAcrossALine_IsRejected()
    {
        const string source = "Fate of the Snow Elves\n<page break>\nSome things.";

        XTranslatorAi.Core.Translation.TokenValidator.ValidateFinalTextIntegrity(source, "스노우 엘프의 운명\n<page break>\n몇 가지.", "test");
        Assert.Throws<InvalidOperationException>(() => XTranslatorAi.Core.Translation.TokenValidator.ValidateFinalTextIntegrity(
            source, "스노우 엘프의 운명\n\n몇 가지.<page break>", "test"));
        Assert.True(LqaScanner.HasTokenMismatch("One.[page break]Two.", "하나. 둘."));
    }

    [Fact]
    public void Sanitizer_StillDropsMarkupTheSourceDoesNotHave()
    {
        Assert.Equal("좋아.", XTranslatorAi.Core.Translation.TokenSanitizer.SanitizeModelTranslationText("<b>좋아.</b>", "<Clears throat> Fine."));
        Assert.Equal("좋아.", XTranslatorAi.Core.Translation.TokenSanitizer.SanitizeModelTranslationText("<font color='#ff0000'>좋아.", "Fine."));
    }

    // Skyrim help messages: the game replaces "[Sprint]" with the player's key, and the official translation
    // keeps it in English ("이동중에 [Sprint] 키를 누르면 질주 합니다"). 1.8 and 1.9 translated it as "[달리기]".
    [Fact]
    public void Mask_ProtectsControlKeysInHelpMessages()
    {
        var masker = new PlaceholderMasker();
        var input = "Press [Ready Weapon], [Left Attack/Block], or [Right Attack/Block] to draw your weapons. Hold [Sprint] to sprint.";

        var masked = masker.Mask(input);

        Assert.Equal("Press __XT_PH_KEY_0000__, __XT_PH_KEY_0001__, or __XT_PH_KEY_0002__ to draw your weapons. Hold __XT_PH_KEY_0003__ to sprint.", masked.Text);
        Assert.Equal("[Sprint]", masked.TokenToOriginal["__XT_PH_KEY_0003__"]);
        Assert.Equal(input, masker.Unmask(masked.Text, masked.TokenToOriginal));
    }

    // Elden Rim writes its own skill names in brackets and translates them ("[Hand Strap]" → "[핸드 스트랩]").
    [Fact]
    public void Mask_LeavesOtherBracketsAndButtonLabelsAsText()
    {
        var masker = new PlaceholderMasker();

        Assert.Equal("Use [Hand Strap] with [sprint] and [Weapon Switch].", masker.Mask("Use [Hand Strap] with [sprint] and [Weapon Switch].").Text);
        Assert.Equal("[Back]", masker.Mask("[Back]").Text);
        Assert.Equal("[pagebreak]", masker.Mask("[pagebreak]").TokenToOriginal["__XT_PH_0000__"]);
    }

    [Fact]
    public void QualityCheck_ReportsATranslatedControlKey()
    {
        Assert.True(XTranslatorAi.Core.Text.LqaScanner.HasTokenMismatch("Hold [Sprint] to sprint while moving.", "이동 중에 [달리기] 키를 누르고 있으면 질주합니다."));
        Assert.False(XTranslatorAi.Core.Text.LqaScanner.HasTokenMismatch("Hold [Sprint] to sprint while moving.", "이동 중에 [Sprint] 키를 누르고 있으면 질주합니다."));
        // A key kept in English is not English left in the translation.
        Assert.Null(XTranslatorAi.Core.Text.LqaScanner.FindEnglishResidue("[Ready Weapon] 키를 눌러 무기를 꺼냅니다.", "Press [Ready Weapon] to draw your weapons."));
    }
}
