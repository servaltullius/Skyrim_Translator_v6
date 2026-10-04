using System.Collections.Generic;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// "A skill beyond the reach of most." opened most War Ash descriptions and came out four ways per pack: identical rows
/// are translated once, but a sentence shared by different rows was not.
/// </summary>
public class RepeatedSentenceSelectionTests
{
    [Fact]
    public void SentenceSharedByThreeDescriptions_IsSelected()
    {
        var rows = new List<(string Masked, string Rec)>
        {
            ("A skill beyond the reach of most. Leap and slash.", "MGEF:DNAM"),
            ("A skill beyond the reach of most. Summon a storm.", "MGEF:DNAM"),
            ("A skill beyond the reach of most. Throw a pot.", "MGEF:DNAM"),
            ("Deals __XT_PH_NUM_0000__ damage. Usable on swords.", "MGEF:DNAM"), // token: never a seed
            ("Deals __XT_PH_NUM_0001__ damage. Usable on swords.", "MGEF:DNAM"),
            ("Deals __XT_PH_NUM_0002__ damage. Usable on swords.", "MGEF:DNAM"),
            ("A skill beyond the reach of most.", "INFO:NAM1"),                  // dialogue does not count
        };

        var sentences = TranslationService.SelectRepeatedSentences(rows, 20);

        Assert.Equal(new[] { "A skill beyond the reach of most.", "Usable on swords." }, sentences);
    }

    [Fact]
    public void SentenceInOnlyTwoRows_IsNotSelected()
        => Assert.Empty(TranslationService.SelectRepeatedSentences(new List<(string, string)>
        {
            ("A skill beyond the reach of most. One.", "MGEF:DNAM"),
            ("A skill beyond the reach of most. Two.", "MGEF:DNAM"),
        }, 20));
}
