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

    // Elden Rim's RimSkills wrote "Rain of Arrow" in 7 rows, which escaped the glossary's "Rain of Arrows" (애로우 레인)
    // and came out as 화살비. A multi-word term in a regular plural also matches its singular.
    [Theory]
    [InlineData("Rain of Arrows", "Rain of Arrow: shoots arrows.", true)]
    [InlineData("Rain of Arrows", "Rain of Arrows: shoots arrows.", true)]
    [InlineData("Rain of Arrows", "Rain of Arrowheads", false)]
    [InlineData("Petty Soul Gems", "A Petty Soul Gem.", true)]
    [InlineData("Arrows", "An Arrow.", false)]
    [InlineData("Call to Arms", "Call to Arm", false)]
    [InlineData("Captain Aldis", "Captain Aldi", false)]
    [InlineData("General Tullius", "General Tulliu", false)]
    [InlineData("Molten Glass", "Molten Glas", false)]
    public void MultiWordPluralTerm_MatchesItsSingular(string source, string text, bool matches)
        => Assert.Equal(matches, Applier(source, "용어").Apply(text).TokenToReplacement.Count > 0);

    // LotD's books write "Summerset Isle" (서머셋 아일), which was forced to the built-in "Summerset Isles" (서머셋 아일즈):
    // a translation that sounds the plural s out is the plural's own.
    [Theory]
    [InlineData("Summerset Isles", "서머셋 아일즈", "The Crystal Tower of Summerset Isle.", false)]
    [InlineData("Summerset Isles", "서머셋 아일즈", "Ships sailed to the Summerset Isles.", true)]
    [InlineData("Shivering Isles", "쉬버링 아일즈", "A Shivering Isle.", false)]
    [InlineData("Atom Cats", "아톰 캣츠", "An Atom Cat.", false)]
    [InlineData("Rain of Arrows", "애로우 레인", "Rain of Arrow", true)]
    // The singular sounds the s too: 팰리스 is the Imperial Palace's as well.
    [InlineData("Imperial Palaces", "임페리얼 팰리스", "The Imperial Palace.", true)]
    public void PluralTermWhoseTranslationSoundsTheS_LeavesTheSingular(string source, string target, string text, bool matches)
        => Assert.Equal(matches, Applier(source, target).Apply(text).TokenToReplacement.Count > 0);

    [Fact]
    public void PluralTerm_LeavesTheSingularToItsOwnEntry()
    {
        var applier = new GlossaryApplier(new[]
        {
            new GlossaryEntry(1, null, "Steel Arrows", "강철 화살 묶음", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
            new GlossaryEntry(2, null, "Steel Arrow", "강철 화살", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
        });

        Assert.Equal("강철 화살", Assert.Single(applier.Apply("A Steel Arrow.").TokenToReplacement).Value);
        Assert.Equal("강철 화살 묶음", Assert.Single(applier.Apply("Some Steel Arrows.").TokenToReplacement).Value);
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
    // A name ending in i takes no plural s: "the days of Aldmeris" (the Aldmeri homeland) was forced to 알드머.
    [InlineData("종족 및 생물 (Races/Creatures)", "Aldmeri", "알드머", "We've lived together since the days of Aldmeris.", false)]
    [InlineData("종족 및 생물 (Races/Creatures)", "Aldmeri", "알드머", "An Aldmeri tribe.", true)]
    [InlineData("진영 및 단체 (Factions/Groups)", "Akaviri", "아카비르", "The Akaviris came.", false)]
    // Only a race or group of one word in -i: a term of more words, or one in -u, keeps its plural.
    [InlineData(null, "Combat AI", "전투 AI", "Two Combat AIs fight.", true)]
    [InlineData("종족 및 생물 (Races/Creatures)", "Wamasu", "와마수", "A herd of Wamasus.", true)]
    public void SingleWordTermOfABeingOrGroup_MatchesItsPlural(string? category, string source, string target, string text, bool matches)
    {
        var applier = new GlossaryApplier(new[] { new GlossaryEntry(1, category, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null) });

        Assert.Equal(matches, applier.Apply(text).TokenToReplacement.Count > 0);
    }

    // The built-in entries force a capitalized single word only in that exact form, so the plural above never reached
    // the built-in Vampire it was written for. A plural is a noun in any case ("vampires", "Giants"); the singular in
    // lowercase may still be an adjective ("a giant spider") and stays a hint.
    [Theory]
    [InlineData("Vampire", "흡혈귀", "A nest of vampires.", true)]
    [InlineData("Vampire", "흡혈귀", "Vampires are powerful.", true)]
    [InlineData("Giant", "거인", "Giants live here.", true)]
    [InlineData("Giant", "거인", "A giant spider.", false)]
    public void BuiltInTermOfABeing_MatchesItsPluralInAnyCase(string source, string target, string text, bool matches)
    {
        var applier = new GlossaryApplier(new[]
        {
            new GlossaryEntry(1, "종족 및 생물 (Races/Creatures)", source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10,
                "Built-in default glossary"),
        });

        Assert.Equal(matches, applier.Apply(text).TokenToReplacement.Count > 0);
    }
}
