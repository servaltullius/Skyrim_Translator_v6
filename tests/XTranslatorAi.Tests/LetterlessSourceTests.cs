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
}
