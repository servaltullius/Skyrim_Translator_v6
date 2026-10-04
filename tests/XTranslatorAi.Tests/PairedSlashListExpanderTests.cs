using System;
using System.Reflection;
using Xunit;

namespace XTranslatorAi.Tests;

public class PairedSlashListExpanderTests
{
    [Fact]
    public void Expand_RewritesPairedSlashSeparatedLists_IntoExplicitMapping()
    {
        var type = Type.GetType("XTranslatorAi.Core.Text.PairedSlashListExpander, XTranslatorAi.Core");
        Assert.NotNull(type);

        var method = type!.GetMethod("Expand", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var input =
            "The amount of reduction __XT_PH_NUM_0000__/__XT_PH_NUM_0001__/__XT_PH_NUM_0002__ per point of Heavy Armor/Light Armor/Alteration skill level you have, with a 7 second cooldown.";

        var expected =
            "The amount of reduction per point of skill level you have: Heavy Armor: __XT_PH_NUM_0000__; Light Armor: __XT_PH_NUM_0001__; Alteration: __XT_PH_NUM_0002__, with a 7 second cooldown.";

        var output = (string)method!.Invoke(null, new object?[] { input })!;
        Assert.Equal(expected, output);
    }

    [Fact]
    public void Expand_DoesNotRewrite_WhenListCountsMismatch()
    {
        var type = Type.GetType("XTranslatorAi.Core.Text.PairedSlashListExpander, XTranslatorAi.Core");
        Assert.NotNull(type);

        var method = type!.GetMethod("Expand", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var input = "__XT_PH_NUM_0000__/__XT_PH_NUM_0001__ per point of Heavy Armor/Light Armor/Alteration.";
        var output = (string)method!.Invoke(null, new object?[] { input })!;

        Assert.Equal(input, output);
    }

    // Line breaks are masked, so the last label took in the next line: "Archery⏎Requires Smithing 50".
    [Fact]
    public void Expand_EndsTheLastLabelAtAMaskedLineBreak()
    {
        var input = "__XT_PH_NUM_0000__/__XT_PH_NUM_0001__/__XT_PH_NUM_0002__ per level of One-handed/Two-handed/Archery__XT_PH_0003__Requires Smithing 50";

        Assert.Equal(
            "per level: One-handed: __XT_PH_NUM_0000__; Two-handed: __XT_PH_NUM_0001__; Archery: __XT_PH_NUM_0002____XT_PH_0003__Requires Smithing 50",
            XTranslatorAi.Core.Text.PairedSlashListExpander.Expand(input));
    }

    [Fact]
    public void Expand_KeepsTermTokenLabels_AndEndsTheSuffixAtALineBreak()
    {
        var input = "__XT_PH_NUM_0000__/__XT_PH_NUM_0001__ per level of __XT_TERM_0002__/__XT_TERM_0003__ skill__XT_PH_0004__Requires Smithing 50";

        Assert.Equal(
            "per level of skill: __XT_TERM_0002__: __XT_PH_NUM_0000__; __XT_TERM_0003__: __XT_PH_NUM_0001____XT_PH_0004__Requires Smithing 50",
            XTranslatorAi.Core.Text.PairedSlashListExpander.Expand(input));
    }

    [Fact]
    public void Expand_DoesNotRewrite_WhenAnItemSpansALineBreak()
    {
        var input = "__XT_PH_NUM_0000__/__XT_PH_NUM_0001__ per level of One-handed__XT_PH_0002__Two-handed/Archery";

        Assert.Equal(input, XTranslatorAi.Core.Text.PairedSlashListExpander.Expand(input));
    }
}

