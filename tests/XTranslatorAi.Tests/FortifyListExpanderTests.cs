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

    [Fact]
    public void Expand_DoesNotChange_WhenAlreadyExpanded()
    {
        var input = "Fortify Armor, Fortify Blocking and Fortify Smithing are <20>% better.";
        Assert.Equal(input, FortifyListExpander.Expand(input));
    }
}

