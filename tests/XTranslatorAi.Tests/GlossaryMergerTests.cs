using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Session term memory adds what it learns to the project glossary as disabled "Auto(Session)" suggestions.
/// Any project entry used to hide the global entry with the same source, so an unreviewed suggestion left the
/// name with no glossary entry at all.
/// </summary>
public class GlossaryMergerTests
{
    private static readonly GlossaryEntry Global =
        new(1, "Places", "Whiterun", "화이트런", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null);

    [Fact]
    public void DisabledAutoSuggestion_DoesNotHideTheGlobalEntry()
    {
        var suggestion = new GlossaryEntry(1, GlossaryMerger.SessionAutoSuggestionCategory, "Whiterun", "화이트런 시", false,
            GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, 20, "Auto-learned suggestion; review before enabling");

        var merged = GlossaryMerger.Merge(new[] { suggestion }, new[] { Global });

        var active = Assert.Single(merged, e => e.Enabled);
        Assert.Equal("화이트런", active.TargetTerm);
    }

    [Fact]
    public void EnabledAutoSuggestion_OverridesTheGlobalEntry()
    {
        var reviewed = new GlossaryEntry(1, GlossaryMerger.SessionAutoSuggestionCategory, "Whiterun", "화이트런 시", true,
            GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, 20, null);

        var merged = GlossaryMerger.Merge(new[] { reviewed }, new[] { Global });

        Assert.Equal("화이트런 시", Assert.Single(merged).TargetTerm);
    }

    // Turning a project entry off is how a user drops a global term for one project.
    [Fact]
    public void EntryTheUserTurnedOff_StillHidesTheGlobalEntry()
    {
        var userEntry = new GlossaryEntry(1, "Places", "Whiterun", "화이트런", false,
            GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null);

        var merged = GlossaryMerger.Merge(new[] { userEntry }, new[] { Global });

        Assert.False(Assert.Single(merged).Enabled);
    }
}
