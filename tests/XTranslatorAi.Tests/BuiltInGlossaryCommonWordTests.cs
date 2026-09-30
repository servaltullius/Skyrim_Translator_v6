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

    [Theory]
    [InlineData("Fine", "하급", "Fine Iron Sword")]
    [InlineData("Master", "달인", "Master of Stealth achievement display")]
    [InlineData("Destruction", "파괴마법", "Increases Destruction spell damage.")]
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
