using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

/// <summary>
/// The term checks read the glossary as the translation applies it (GlossaryApplier). The missing-term check had a term
/// search of its own that knew no plural or singular form, the TM name check took only forced terms for the user's
/// choice, and the checks' strip of UI text kept control keys ("[Sneak]") the translation protects.
/// </summary>
public sealed class LqaGlossaryLikeTranslationTests
{
    private static GlossaryEntry Term(long id, string source, string target, GlossaryForceMode force = GlossaryForceMode.ForceToken,
        string? category = null, string? note = null)
        => new(id, category, source, target, true, GlossaryMatchMode.WordBoundary, force, 10, note);

    [Fact]
    public void AMultiWordTermsSingular_TakesItsText_AsInTheTranslation()
    {
        var glossary = new[] { Term(1, "Rain of Arrows", "애로우 레인"), Term(2, "Arrow", "화살") };

        // The translation forces "Rain of Arrows" over "Rain of Arrow": "Arrow" is no term of its own there.
        Assert.Null(LqaHeuristics.FindMissingForceTokenGlossaryTerm("Rain of Arrow", "애로우 레인", glossary));
        Assert.Equal(1, LqaHeuristics.FindMissingForceTokenGlossaryTerm("Rain of Arrow", "화살비", glossary)?.Id);
    }

    [Fact]
    public void AMultiWordTermsPlural_IsLookedFor()
        => Assert.Equal(1, LqaHeuristics.FindMissingForceTokenGlossaryTerm("NPC Weapon Arts", "NPC 전투 기술", new[] { Term(1, "Weapon Art", "전기") })?.Id);

    /// <summary>Races and groups are named in the plural too ("vampires" for the built-in Vampire).</summary>
    [Fact]
    public void ARacesPlural_IsLookedFor()
    {
        var vampire = Term(1, "Vampire", "흡혈귀", category: "Races/Creatures", note: "Built-in default glossary");
        Assert.Equal(1, LqaHeuristics.FindMissingForceTokenGlossaryTerm("The vampires attacked.", "뱀파이어들이 공격했다.", new[] { vampire })?.Id);
        Assert.Null(LqaHeuristics.FindMissingForceTokenGlossaryTerm("The vampires attacked.", "흡혈귀들이 공격했다.", new[] { vampire }));
    }

    /// <summary>A control key stays in English on purpose: "[Sneak]" is no missing 은신.</summary>
    [Fact]
    public void AControlKey_IsNoTerm()
        => Assert.Null(LqaHeuristics.FindMissingForceTokenGlossaryTerm("Press [Sneak] to hide.", "[Sneak] 키를 눌러 숨으세요.",
            new[] { Term(1, "Sneak", "은신", note: "Built-in default glossary") }));

    /// <summary>A name a prompt-only term sets is the user's choice, as the translation leaves it (GlossaryApplier.HasTerm).</summary>
    [Fact]
    public async Task AnOfficialNameAPromptOnlyTermSets_IsNotReported()
    {
        var names = ReferenceNameIndex.Build(new[] { ("Jorrvaskr", "요르바스카"), ("Meet me in Jorrvaskr.", "요르바스카에서 만나자.") });
        var row = new LqaScanEntry(1, 1, "SDA_Topic", "INFO:NAM1", StringEntryStatus.Done, "Let's go to Jorrvaskr.", "조르바스크로 가자.");

        Assert.Contains(await LqaScanner.ScanAsync(new[] { row }, "ko", new List<GlossaryEntry>(), referenceNames: names),
            i => i.Code == "official_name_missing");
        var glossary = ProjectLqaScan.CheckGlossary(new[]
        {
            Term(1, "Jorrvaskr", "조르바스크", GlossaryForceMode.PromptOnly), Term(2, "Whiterun", "화이트런"),
        }, null);
        Assert.DoesNotContain(await LqaScanner.ScanAsync(new[] { row }, "ko", glossary, referenceNames: names),
            i => i.Code == "official_name_missing");
        // Not a forced term: it is not looked for as one (a forced term is there, so the search runs).
        Assert.DoesNotContain(await LqaScanner.ScanAsync(new[] { row with { DestText = "요르바스카로 가자." } }, "ko", glossary),
            i => i.Code == "glossary_missing");
    }

    /// <summary>A singular with a prompt-only term of its own is not the plural's, as in the translation.</summary>
    [Fact]
    public void ASingularWithAPromptOnlyTerm_IsNotThePlurals()
        => Assert.Null(LqaHeuristics.FindMissingForceTokenGlossaryTerm("Cast Rain of Arrow now.", "화살비를 써.", new[]
        {
            Term(1, "Rain of Arrows", "애로우 레인"), Term(2, "Rain of Arrow", "화살비", GlossaryForceMode.PromptOnly),
        }));

    /// <summary>The check's forced terms are those the translation forces: Apply's tokens and ForcedEntries name the same terms.</summary>
    [Theory]
    [InlineData("Rain of Arrow")]
    [InlineData("The vampires and a giant spider.")]
    [InlineData("What in Oblivion is this? Reach level 10.")]
    [InlineData("The Dragon Priest and the Dragon.")]
    [InlineData("A Master's approval. Master Alteration.")]
    public void ForcedEntries_AreWhatApplyForces(string text)
    {
        var applier = new GlossaryApplier(new[]
        {
            Term(1, "Rain of Arrows", "애로우 레인"), Term(2, "Arrow", "화살"),
            Term(3, "Vampire", "흡혈귀", category: "Races/Creatures", note: "Built-in default glossary"),
            Term(4, "Giant", "거인", category: "Races/Creatures", note: "Built-in default glossary"),
            Term(5, "Oblivion", "오블리비언", note: "Built-in default glossary"), Term(6, "Reach", "리치", note: "Built-in default glossary"),
            new GlossaryEntry(7, null, "Dragon ?Priest", "드래곤 프리스트", true, GlossaryMatchMode.Regex, GlossaryForceMode.ForceToken, 20, null),
            Term(8, "Dragon", "용"), Term(9, "Master", "달인", note: "Built-in default glossary"),
        });

        var applied = applier.Apply(text).TokenToReplacement.Values.OrderBy(t => t, StringComparer.Ordinal);
        Assert.Equal(applied, applier.ForcedEntries(text).Select(e => e.TargetTerm).OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>
    /// The untranslated check reads control keys as text (the translation's quality escalation reads it too), but a line
    /// of nothing but control keys has nothing to translate.
    /// </summary>
    [Fact]
    public void TheUntranslatedCheck_ReadsControlKeys_ButNotALineOfThemAlone()
    {
        Assert.True(LqaHeuristics.IsLikelyUntranslated("[Activate] Talk", "[Activate] Talk"));
        Assert.False(LqaHeuristics.IsLikelyUntranslated("[Sneak] [Sprint]", "[Sneak] [Sprint]"));
    }
}
