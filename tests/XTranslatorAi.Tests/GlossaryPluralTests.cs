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

    // Feris wrote "vampires" as 뱀파이어 in 10 lines: the built-in Vampire (흡혈귀) matched only the singular. Races,
    // creatures and groups are named in the plural all the time, so their single words match it too; other single
    // words keep their exact form ("resists" is a verb, "shores" is not Shor, "masters" is not the rank 달인).
    [Theory]
    [InlineData("종족 및 생물 (Races/Creatures)", "Vampire", "흡혈귀", "A nest of vampires.", true)]
    [InlineData("진영 및 단체 (Factions/Groups)", "Bandit", "산적", "Bandits ahead.", true)]
    [InlineData("신화 및 주요 존재 (Mythology & Key Beings)", "Shor", "쇼어", "Along the shores.", false)]
    [InlineData("능력치 및 효과 (Attributes & Effects)", "Resist", "저항", "He resists.", false)]
    public void SingleWordTermOfABeingOrGroup_MatchesItsPlural(string category, string source, string target, string text, bool matches)
    {
        var applier = new GlossaryApplier(new[] { new GlossaryEntry(1, category, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null) });

        Assert.Equal(matches, applier.Apply(text).TokenToReplacement.Count > 0);
    }
}
