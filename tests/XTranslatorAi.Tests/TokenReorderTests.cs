using System;
using System.Collections.Generic;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

// Korean word order often reverses English noun phrases. The token repair used to
// rewrite every token back into source order, which swapped terms and aliases
// ("Jarl of Whiterun" → "야를의 화이트런", "<Alias=Evidence>에 있는 … <Alias=City>")
// in the 2026-09-30 quality evaluation.
public class TokenReorderTests
{
    private const string Jarl = "__XT_TERM_G1_0000__";
    private const string Whiterun = "__XT_TERM_G2_0000__";

    [Fact]
    public void ReorderedGlossaryTerms_AreAccepted()
    {
        var input = $"the {Jarl} of {Whiterun}";
        var output = $"{Whiterun}의 {Jarl}";

        TokenValidator.ValidateTokensPreserved(input, output, "test");
        Assert.Equal(output, TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test"));
    }

    [Fact]
    public void AliasPlaceholders_AreMaskedAsRuntimeValues()
    {
        var masked = new PlaceholderMasker().Mask("Plant the <Alias=Evidence> in <Alias=WealthyHome> in <Alias.Pronoun=City>");

        Assert.Equal("Plant the __XT_PH_VAR_0000__ in __XT_PH_VAR_0001__ in __XT_PH_VAR_0002__", masked.Text);
    }

    [Theory]
    [InlineData("<b>Hello</b>", "__XT_PH_0000__Hello__XT_PH_0001__")]
    [InlineData("<font face='x'>Hi</font>", "__XT_PH_0000__Hi__XT_PH_0001__")]
    [InlineData("A\nB[pagebreak]C", "A__XT_PH_0000__B__XT_PH_0001__C")]
    [InlineData("<custom>Hi</custom>", "__XT_PH_0000__Hi__XT_PH_0001__")]
    public void FormattingPlaceholders_KeepFixedTokens(string input, string expected)
    {
        Assert.Equal(expected, new PlaceholderMasker().Mask(input).Text);
    }

    [Fact]
    public void ReorderedAliases_AreKept()
    {
        var input = "Plant the __XT_PH_VAR_0000__ in __XT_PH_VAR_0001__ in __XT_PH_VAR_0002__";
        var output = "__XT_PH_VAR_0002__에 있는 __XT_PH_VAR_0001__에 __XT_PH_VAR_0000__ 몰래 두기";

        Assert.Equal(output, TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test"));
    }

    [Fact]
    public void TermMovingAcrossWrappedLineBreak_IsAccepted()
    {
        // A journal wrapped mid-sentence (E0302): Korean puts "Iliac Bay" before "Markarth".
        const string markarth = "__XT_TERM_G5_0000__";
        const string iliac = "__XT_TERM_G6_0000__";
        var input = $"my father took me on a trip to__XT_PH_0000__{markarth}, to sell supplies brought from the {iliac}.";
        var output = $"아버지가 {iliac}에서 가져온 물자를 팔기 위해__XT_PH_0000__{markarth}로 나를 데려갔다.";

        TokenValidator.ValidateTokensPreserved(input, output, "test");
    }

    [Fact]
    public void NumericTokenMovingAcrossLineBreak_IsStillRejected()
    {
        var input = "Damage __XT_PH_MAG_0000____XT_PH_0001__Lasts __XT_PH_DUR_0002__";
        var output = "지속 __XT_PH_DUR_0002____XT_PH_0001__피해 __XT_PH_MAG_0000__";

        Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateTokensPreserved(input, output, "test"));
    }

    [Fact]
    public void LinesShiftedIntoEmptySlots_AreRejected()
    {
        // E0580: a duplicated header line pushed every poem line up, past "</p>" and the page breaks.
        var input = "Title__XT_PH_0000__Sage__XT_PH_0001____XT_PH_0002____XT_PH_0003____XT_PH_0004__One__XT_PH_0005__Two__XT_PH_0006__Three__XT_PH_0007__Four";
        var output = "제목__XT_PH_0000__현자__XT_PH_0001__현자__XT_PH_0002__하나__XT_PH_0003__둘__XT_PH_0004__셋__XT_PH_0005__넷__XT_PH_0006____XT_PH_0007__";

        Assert.Throws<InvalidOperationException>(() => TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test"));
    }

    [Fact]
    public void OneLineJoinedAfterAnOpeningTag_IsAccepted()
    {
        // E0319: "<font …>" and the first sentence on one line, the line break moved to a paragraph gap.
        var input = "__XT_PH_0000____XT_PH_0001__Here you will find my research.__XT_PH_0002____XT_PH_0003__The Hall";
        var output = "__XT_PH_0000__여기에 내 연구가 있다.__XT_PH_0001____XT_PH_0002____XT_PH_0003__영웅의 전당";

        Assert.Equal(output, TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test"));
    }

    [Fact]
    public void TitleSplitOverTwoLines_MayBeJoined()
    {
        var input = "__XT_PH_0000__A Short History __XT_PH_0001__of Morrowind__XT_PH_0002__";
        var output = "__XT_PH_0000__모로윈드 약사__XT_PH_0001____XT_PH_0002__";

        Assert.Equal(output, TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test"));
    }

    [Fact]
    public void AlignedLines_AndTermOnlyLines_AreAccepted()
    {
        var input = "Title__XT_PH_0000____XT_PH_0001____XT_PH_0002__(1)__XT_PH_0003__" + Jarl;
        var output = "제목__XT_PH_0000____XT_PH_0001____XT_PH_0002__(1)__XT_PH_0003__" + Jarl;

        Assert.Equal(output, TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test"));
    }

    [Fact]
    public void FixedTokens_StillMustKeepOrder()
    {
        var input = "A __XT_PH_0000__ B __XT_PH_0001__ C";
        var output = "A __XT_PH_0001__ B __XT_PH_0000__ C";

        Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateTokensPreserved(input, output, "test"));
    }

    [Fact]
    public void Repair_RestoresFixedOrder_WithoutSwappingTerms()
    {
        var input = $"__XT_PH_0000__the {Jarl} of {Whiterun}__XT_PH_0001__";
        var output = $"__XT_PH_0001__{Whiterun}의 {Jarl}__XT_PH_0000__";

        Assert.True(TokenValidator.TryRepairTokens(input, output, null, out var repaired));
        Assert.Equal($"__XT_PH_0000__{Whiterun}의 {Jarl}__XT_PH_0001__", repaired);
    }

    [Fact]
    public void MissingTerm_IsNotAppendedToTheEnd()
    {
        const string nord = "__XT_TERM_G3_0000__";
        var input = $"The {Jarl} and the {nord} fought.";
        var output = $"{Jarl}과 다크 엘프가 싸웠다.";
        var glossary = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Jarl] = "야를",
            [nord] = "노드",
        };

        Assert.Throws<InvalidOperationException>(() => TokenSanitizer.EnsureTokensPreservedOrRepair(input, output, "test", glossary));
    }
}
