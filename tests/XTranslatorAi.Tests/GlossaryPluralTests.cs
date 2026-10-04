using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// "NPCs with this PERK cannot use NPC Weapon Arts." kept "Weapon Arts" as plain text because the glossary's "Weapon
/// Art" (전기) matched only the singular, and the evaluation got 전투 기술. Korean does not mark the plural, so a
/// multi-word term now also matches with a plural s or es.
/// </summary>
public class GlossaryPluralTests
{
    private static GlossaryApplier Applier(string source, string target)
        => new(new[] { new GlossaryEntry(1, null, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null) });

    [Theory]
    [InlineData("NPCs cannot use NPC Weapon Arts.", true)]
    [InlineData("Use a Weapon Art now.", true)]
    [InlineData("Weapon Artistry is different.", false)]
    public void MultiWordTerm_MatchesItsPlural(string text, bool matches)
    {
        var applied = Applier("Weapon Art", "전기").Apply(text);

        Assert.Equal(matches, applied.TokenToReplacement.Count > 0);
        if (matches)
        {
            Assert.DoesNotContain("Weapon Art", applied.Text);
        }
    }

    [Fact]
    public void SingleWordTerm_StillMatchesOnlyAsWritten()
        => Assert.Empty(Applier("Rune", "룬").Apply("Runes glow.").TokenToReplacement);
}
