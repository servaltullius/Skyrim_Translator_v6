using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class FortifyListExpanderTests
{
    [Fact]
    public void Expand_ExpandsSharedPrefixLists_WithAnd()
    {
        var input = "Fortify Armor, Blocking and Smithing are <20>% better.";
        var expected = "Fortify Armor, Fortify Blocking and Fortify Smithing are <20>% better.";

        Assert.Equal(expected, FortifyListExpander.Expand(input));
    }

    [Fact]
    public void Expand_PreservesOxfordCommaStyle()
    {
        var input = "Fortify Armor, Blocking, and Smithing are <20>% better.";
        var expected = "Fortify Armor, Fortify Blocking, and Fortify Smithing are <20>% better.";

        Assert.Equal(expected, FortifyListExpander.Expand(input));
    }

    [Fact]
    public void Expand_ExpandsTwoItemLists()
    {
        var input = "Fortify Archery and One-handed are <20>% better.";
        var expected = "Fortify Archery and Fortify One-handed are <20>% better.";

        Assert.Equal(expected, FortifyListExpander.Expand(input));
    }

    [Fact]
    public void Expand_DoesNotChange_WhenSingleItem()
    {
        var input = "Fortify Smithing is <20>% better.";
        Assert.Equal(input, FortifyListExpander.Expand(input));
    }

    // The list ran across clauses: "Fortify Sneak is active, Fortify and enemies and Fortify guards are less alert."
    [Theory]
    [InlineData("Fortify Sneak is active, and enemies and guards are less alert.")]
    [InlineData("While Fortify Sneak is active, guards and enemies are less alert.")]
    [InlineData("Fortify Sneak Is Active, And Enemies Are Less Alert.")]
    [InlineData("Fortify Health regenerates faster, and Stamina and Magicka are restored.")]
    public void Expand_DoesNotChange_WhenTheWordsAfterFortifyAreAClause(string input)
    {
        Assert.Equal(input, FortifyListExpander.Expand(input));
    }

    [Fact]
    public void Expand_ExpandsMultiWordTerms()
    {
        Assert.Equal(
            "Fortify Light Armor, Fortify Heavy Armor and Fortify Block are __XT_PH_NUM_0000__ better.",
            FortifyListExpander.Expand("Fortify Light Armor, Heavy Armor and Block are __XT_PH_NUM_0000__ better."));
    }

    [Fact]
    public void Expand_DoesNotChange_WhenAlreadyExpanded()
    {
        var input = "Fortify Armor, Fortify Blocking and Fortify Smithing are <20>% better.";
        Assert.Equal(input, FortifyListExpander.Expand(input));
    }
}

