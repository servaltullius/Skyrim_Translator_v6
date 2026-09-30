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
