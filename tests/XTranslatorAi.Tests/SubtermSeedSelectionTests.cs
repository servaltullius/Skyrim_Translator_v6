using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// "Deathblow" recurs inside 91 Elden Rim names and descriptions but is never a row of its own, so session term memory
/// never learned it and it came out in seven spellings. Such parts are now translated once before the run.
/// </summary>
public class SubtermSeedSelectionTests
{
    private static (string, string, string) Row(string source, string rec) => (source, rec, source);

    [Fact]
    public void RecurringNamePart_IsSelected_OrdinaryAndKnownWordsAreNot()
    {
        var rows = new List<(string Source, string Rec, string Masked)>
        {
            Row("Parry - Deathblow Paralyzed", "SPEL:FULL"),
            Row("Hotkey Deathblow - Visceral Blast", "SPEL:FULL"),
            Row("Elden Deathblow", "PERK:FULL"),
            Row("If the enemy is hit, Deathblow is allowed.", "SPEL:DESC"),
            Row("Deathblow", "SPEL:DESC"),                       // a description, not a name row the seeding uses
            Row("Ancient Sword", "WEAP:FULL"),
            Row("Ancient Shield", "ARMO:FULL"),
            Row("Ancient Helmet", "ARMO:FULL"),
            Row("An ancient blade of old.", "WEAP:DESC"),       // "Ancient" is an ordinary word here
            Row("Rebreath - Lv1", "SPEL:FULL"),
            Row("Rebreath - Lv2", "SPEL:FULL"),
            Row("Rebreath - Lv3", "SPEL:FULL"),
            Row("Use Rebreath to recover.", "SPEL:DESC"),
        };

        var seeds = TranslationService.SelectSubtermSeeds(rows, term => term == "Rebreath", 24);

        Assert.Equal(new[] { "Deathblow" }, seeds);
    }

    [Fact]
    public void PartAlreadyTokenizedByTheGlossary_IsNotSelected()
    {
        var rows = Enumerable.Range(1, 4)
            .Select(i => ($"Weapon Art - Skill {i}", "SPEL:FULL", $"__XT_TERM_G1_0000__ - Skill {i}"))
            .ToList();

        Assert.Empty(TranslationService.SelectSubtermSeeds(rows, _ => false, 24));
    }
}

public class SubtermGlossaryCoverageTests
{
    // The evaluation lost "NPC Weapon Arts" → 전기: the plural escaped the glossary token and was seeded as 전투 기술.
    [Theory]
    [InlineData("Weapon Art", true)]
    [InlineData("Weapon Arts", true)]
    [InlineData("NPC Weapon Arts", true)]
    [InlineData("Deathblow", false)]
    public void GlossaryTermsAndTheirPlurals_AreLeftToTheGlossary(string term, bool covered)
    {
        var glossary = new XTranslatorAi.Core.Text.GlossaryApplier(new[]
        {
            new XTranslatorAi.Core.Text.GlossaryEntry(1, null, "Weapon Art", "전기", true,
                XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary, XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, 10, null),
        });

        Assert.Equal(covered, TranslationService.IsGlossaryTerm(glossary, term));
    }
}
