using XTranslatorAi.Core.Models;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// 3,848 of 624,924 fields in 4,913 local mods have no letters at all ("...", a space, a line break, "???", "(...)",
/// "11", an empty paragraph tag). They were sent to the model, which costs a request slot and can come back empty
/// for a blank source, which a required field cannot save. Such a row keeps its source text without a request.
/// </summary>
public class LetterlessSourceTests
{
    [Fact]
    public async Task RowsWithoutLetters_KeepTheirSource_WithoutARequest()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("...", "MESG:DESC"), (" ", "WEAP:FULL"), ("???", "INFO:NAM1"), ("<p align='center'>\n</p>", "BOOK:DESC"), ("11", "MISC:FULL"));

        await fixture.Service.TranslateIdsAsync(fixture.Request);

        Assert.Empty(fixture.Client.Requests);
        Assert.All((await fixture.RowsAsync()).Values, row =>
        {
            Assert.Equal(StringEntryStatus.Done, row.Status);
            Assert.Equal(row.SourceText, row.DestText);
        });
    }

    [Fact]
    public async Task RowsWithLetters_AreStillTranslated()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Iron Sword", "WEAP:FULL"), ("...", "MESG:DESC"), ("战技", "SPEL:FULL"));

        await fixture.Service.TranslateIdsAsync(fixture.Request);

        Assert.NotEmpty(fixture.Client.Requests);
        var rows = (await fixture.RowsAsync()).Values.ToList();
        Assert.All(rows, row => Assert.Equal(StringEntryStatus.Done, row.Status));
        Assert.Equal("...", rows.Single(r => r.SourceText == "...").DestText);
    }

    /// <summary>
    /// A hidden dialogue topic's name is an identifier the player never sees; the quality check says to leave it as is
    /// and the Serana review put 8 of 11 translated ones back. They were still translated ("FeedingTest" → "흡혈 테스트").
    /// </summary>
    [Fact]
    public async Task HiddenTopicIdentifiers_KeepTheirSource_WithoutARequest()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("SDA_DA09IntroTopic00", "DIAL:FULL"), ("FeedingTest", "DIAL:FULL"), ("What do you want?", "DIAL:FULL"), ("Mud_Crab", "RACE:FULL"));

        await fixture.Service.TranslateIdsAsync(fixture.Request);

        var rows = (await fixture.RowsAsync()).Values.ToList();
        Assert.Equal("SDA_DA09IntroTopic00", rows.Single(r => r.SourceText == "SDA_DA09IntroTopic00").DestText);
        Assert.Equal("FeedingTest", rows.Single(r => r.SourceText == "FeedingTest").DestText);
        // Sent as text to translate: a batch item ("text":"…") or a single text block (<<<TEXT …). The neighbouring
        // topic may still list it as reference context.
        var sent = string.Join(" | ", fixture.Client.Requests.Select(r => r.Contents[0].Parts[0].Text));
        static bool Translated(string prompts, string source)
            => System.Text.RegularExpressions.Regex.IsMatch(prompts,
                "\"text\"\\s*:\\s*\"" + source + "|<<<TEXT\\s+" + source);
        Assert.False(Translated(sent, "SDA_DA09IntroTopic00"));
        Assert.False(Translated(sent, "FeedingTest"));
        Assert.True(Translated(sent, "What do you want"));
        Assert.True(Translated(sent, "Mud_Crab"));
    }
}
