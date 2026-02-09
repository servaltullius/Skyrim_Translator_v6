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

        var bad = masked.Text.Replace("__XT_PH_0000__", "MISSING", StringComparison.Ordinal);
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
        Assert.Contains("__XT_PH_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("%PLAYERNAME%", masked.TokenToOriginal["__XT_PH_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksPrintfPositionalSpecifiers_AsSinglePlaceholder()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello %1$s.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("%1$s", masked.TokenToOriginal["__XT_PH_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksDollarWrappedIdentifiers()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello $PLAYERNAME$.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("$PLAYERNAME$", masked.TokenToOriginal["__XT_PH_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksBraceWrappedIdentifiers()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello {PLAYERNAME}.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("{PLAYERNAME}", masked.TokenToOriginal["__XT_PH_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Mask_MasksDoubleBraceWrappedIdentifiers()
    {
        var masker = new PlaceholderMasker();
        var input = "Hello {{PLAYERNAME}}.";

        var masked = masker.Mask(input);
        Assert.Contains("__XT_PH_0000__", masked.Text, StringComparison.Ordinal);
        Assert.Equal("{{PLAYERNAME}}", masked.TokenToOriginal["__XT_PH_0000__"]);

        var output = masker.Unmask(masked.Text, masked.TokenToOriginal);
        Assert.Equal(input, output);
    }
}
