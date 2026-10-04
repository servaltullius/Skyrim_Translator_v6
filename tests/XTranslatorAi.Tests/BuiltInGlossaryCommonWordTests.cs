using System.Linq;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

// Built-in glossary terms that are also ordinary English words were forced into every
// sentence in the 2026-09-30 quality evaluation: "fine blond hair" → "하급 금발",
// "just fine" → "하급다", "superior officer" → "중급 지휘관", "master" → "달인님".
public class BuiltInGlossaryCommonWordTests
{
    private static GlossaryApplier BuiltIn(string source, string target)
        => new(new[]
        {
            new GlossaryEntry(
                Id: 1,
                Category: null,
                SourceTerm: source,
                TargetTerm: target,
                Enabled: true,
                MatchMode: GlossaryMatchMode.WordBoundary,
                ForceMode: GlossaryForceMode.ForceToken,
                Priority: 10,
                Note: "Built-in default glossary"
            ),
        });

    [Theory]
    [InlineData("Fine", "하급", "And my fine blond hair,")]
    [InlineData("Fine", "하급", "Nothing changes in the City of Stone, and that's just fine.")]
    [InlineData("Superior", "중급", "Your superior officer (and loving father),")]
    [InlineData("Master", "달인", "I am but a poor Argonian maid, master.")]
    [InlineData("Destruction", "파괴마법", "They cannot speak without causing destruction.")]
    [InlineData("Ward", "방어막", "I placed a magic ward to suspend their powers.")]
    [InlineData("Sneak", "은신", "We managed to sneak up on a small sloop.")]
    public void LowercaseCommonWord_IsNotForced_ButOfferedAsHint(string source, string target, string input)
    {
        var applied = BuiltIn(source, target).Apply(input);

        Assert.Equal(input, applied.Text);
        Assert.Empty(applied.TokenToReplacement);
        Assert.Contains((source, target), applied.PromptOnlyPairs);
    }

    // Capitalized only because of where they stand: "Fine. I'll do it." became "하급.", "Yes, Master." "네, 달인.".
    [Theory]
    [InlineData("Fine", "하급", "Fine. I'll do it.")]
    [InlineData("Fine", "하급", "Trying to be romantic now? Fine, lead the way.")]
    [InlineData("Master", "달인", "Yes, Master.")]
    [InlineData("Master", "달인", "Master, I have returned.")]
    [InlineData("Reach", "리치", "\"Reach the summit before dawn,\" he said.")]
    [InlineData("Pale", "페일", "Pale light shines through the window.")]
    [InlineData("Fortify", "강화", "Guards! Fortify the gates!")]
    [InlineData("Block", "막기", "Block the next attack, then strike.")]
    public void WordCapitalizedByPosition_IsNotForced_ButOfferedAsHint(string source, string target, string input)
    {
        var applied = BuiltIn(source, target).Apply(input);

        Assert.Equal(input, applied.Text);
        Assert.Contains((source, target), applied.PromptOnlyPairs);
    }

    [Theory]
    [InlineData("Fine", "하급", "Fine Iron Sword")]
    [InlineData("Fine", "하급", "Fine")]
    [InlineData("Master", "달인", "Master of Stealth achievement display")]
    [InlineData("Destruction", "파괴마법", "Increases Destruction spell damage.")]
    [InlineData("Fortify", "강화", "Fortify Health")]
    [InlineData("Resist", "저항", "Resist __XT_PH_MAG_0000__% of magic.")]
    [InlineData("Reach", "리치", "Travel to the Reach. It is dangerous.")]
    [InlineData("Destruction", "파괴마법", "Destruction spells cost 15% less magicka.")]
    [InlineData("Talos", "탈로스", "Talos, guide my hand.")]
    [InlineData("Master", "달인", "Lockpicking rank: Master")]
    [InlineData("Master", "달인", "Apprentice, Adept, Expert, Master, and so on.")]
    public void GameTermCasing_IsStillForced(string source, string target, string input)
    {
        var applied = BuiltIn(source, target).Apply(input);

        Assert.Contains("__XT_TERM_G1_0000__", applied.Text);
        Assert.Equal(target, applied.TokenToReplacement["__XT_TERM_G1_0000__"]);
    }

    [Fact]
    public void UserGlossaryEntry_KeepsCaseInsensitiveMatching()
    {
        var applier = new GlossaryApplier(new[]
        {
            new GlossaryEntry(1, null, "Fine", "하급", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
        });

        Assert.Equal("__XT_TERM_G1_0000__ work.", applier.Apply("fine work.").Text);
    }

    [Fact]
    public void MultiWordBuiltInTerm_KeepsCaseInsensitiveMatching()
    {
        var applied = BuiltIn("East Empire company", "동제국 회사").Apply("the East Empire Company flag");

        Assert.Equal("the __XT_TERM_G1_0000__ flag", applied.Text);
    }

    [Fact]
    public void ElderScroll_IsNotTranslatedAsSpellScroll()
    {
        var applier = new GlossaryApplier(new[]
        {
            new GlossaryEntry(1, null, "Scroll", "주문서", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, "Built-in default glossary"),
            new GlossaryEntry(2, null, "Elder Scroll", "엘더스크롤", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, "Built-in default glossary"),
        });

        var applied = applier.Apply("An Elder Scroll, of course.");

        Assert.Equal("An __XT_TERM_G2_0000__, of course.", applied.Text);
        Assert.Equal("엘더스크롤", applied.TokenToReplacement.Values.Single());
    }
}
