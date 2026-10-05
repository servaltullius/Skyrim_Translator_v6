using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class ReplaceTokenDedupAdjacentTests
{
    private const string Token = "__XT_TERM_SESS_0000__";
    private const string Replacement = "\uc5d8\ub4e0"; // 엘든

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public void AdjacentRepeatedTokens_PreserveBothOccurrences(string separator)
    {
        var result = TranslationService.ReplaceTokenDedupAdjacent($"{Token}{separator}{Token}", Token, Replacement);
        Assert.Equal($"{Replacement}{separator}{Replacement}", result);
    }

    [Fact]
    public void DifferentTokensWithSameReplacement_PreserveBothOccurrences()
    {
        const string second = "__XT_TERM_G2_0000__";
        var result = TranslationService.ReplaceGlossaryTokens($"{Token} {second}", new Dictionary<string, string>
        {
            [Token] = Replacement,
            [second] = Replacement,
        });
        Assert.Equal($"{Replacement} {Replacement}", result);
    }

    [Fact]
    public void DuplicateBefore_RemovesToken()
    {
        var input = $"{Replacement} {Token} \uce74\uc6b4\ud130";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \uce74\uc6b4\ud130", result);
    }

    [Fact]
    public void DuplicateAfter_RemovesToken()
    {
        var input = $"{Token} {Replacement} \uce74\uc6b4\ud130";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \uce74\uc6b4\ud130", result);
    }

    // Silver is 은 in the official translation; the topic particle before or after the token is not a duplicate.
    [Theory]
    [InlineData("네 검은 __XT_TERM_N1_0000__ 검이지.", "네 검은 은 검이지.")]
    [InlineData("__XT_TERM_N1_0000__은 희귀하다.", "은은 희귀하다.")]
    public void OneSyllableReplacement_IsNeverTreatedAsADuplicate(string input, string expected)
        => Assert.Equal(expected, TranslationService.ReplaceTokenDedupAdjacent(input, "__XT_TERM_N1_0000__", "은"));

    // After the token as well, the duplicate must be the word itself, maybe with a particle: 겨울잠 (hibernation) and
    // 가죽 갑옷 start with the terms 겨울 and 가죽, and the term was dropped as a duplicate.
    [Theory]
    [InlineData("__XT_TERM_N1_0000__ 겨울잠을 잔다.", "겨울", "겨울 겨울잠을 잔다.")]
    [InlineData("__XT_TERM_N1_0000__ 겨울은 춥다.", "겨울", "겨울은 춥다.")]
    [InlineData("__XT_TERM_N1_0000__ 가죽 갑옷", "가죽", "가죽 갑옷")]
    public void DuplicateAfter_MustBeTheWordItself(string input, string replacement, string expected)
        => Assert.Equal(expected, TranslationService.ReplaceTokenDedupAdjacent(input, "__XT_TERM_N1_0000__", replacement));

    [Fact]
    public void DuplicateBefore_MustBeAWholeWord()
        => Assert.Equal("초강철 강철 검", TranslationService.ReplaceTokenDedupAdjacent("초강철 __XT_TERM_N1_0000__ 검", "__XT_TERM_N1_0000__", "강철"));

    [Fact]
    public void NoDuplicate_NormalReplacement()
    {
        var input = $"{Token} \uce74\uc6b4\ud130";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \uce74\uc6b4\ud130", result);
    }

    [Fact]
    public void SameTokenTwice_LegitimateRepetition()
    {
        var input = $"{Token} and {Token}";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} and {Replacement}", result);
    }

    [Fact]
    public void EmptyReplacement_FallsBackToStandardReplace()
    {
        var input = $"hello {Token} world";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, "");
        Assert.Equal("hello  world", result);
    }

    [Fact]
    public void DuplicateBeforeNoSpace_RemovesToken()
    {
        var input = $"{Replacement}{Token} \uce74\uc6b4\ud130";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \uce74\uc6b4\ud130", result);
    }

    [Fact]
    public void DuplicateAfterNoSpace_RemovesToken()
    {
        var input = $"{Token}{Replacement} \uce74\uc6b4\ud130";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \uce74\uc6b4\ud130", result);
    }

    [Fact]
    public void DuplicateAfterWithTrailingUnderscores_StripsArtifact()
    {
        // Model outputs: TOKEN + replacement + __ (common AI artifact)
        var input = $"{Token}{Replacement}__ \ud559\ud30c\ub294";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \ud559\ud30c\ub294", result);
    }

    [Fact]
    public void DuplicateBeforeWithTrailingUnderscores_StripsArtifact()
    {
        // Model outputs: replacement + __ + TOKEN
        var input = $"{Replacement}__{Token} \ud559\ud30c\ub294";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \ud559\ud30c\ub294", result);
    }

    [Fact]
    public void DuplicateAfterWithSpaceAndTrailingUnderscores_StripsArtifact()
    {
        var input = $"{Token} {Replacement}__ \ud559\ud30c\ub294";
        var result = TranslationService.ReplaceTokenDedupAdjacent(input, Token, Replacement);
        Assert.Equal($"{Replacement} \ud559\ud30c\ub294", result);
    }
}
