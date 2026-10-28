using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>What the text-only (single-row) path saves from the model's plain-text answer.</summary>
public sealed class TranslationServiceTextOutputTests
{
    // The single-row path trimmed the model output, so "Gold: " was saved as "골드:" and whatever the game
    // appends after the label ran into it. Long-text chunks already restored the source's edge whitespace.
    [Theory]
    [InlineData("Gold: ", "골드: __XT_PH_9999__", "골드: ")]
    [InlineData("Gold: ", "골드:", "골드: ")]
    [InlineData("  Indented", "들여쓰기 __XT_PH_9999__", "  들여쓰기")]
    [InlineData("Hello", "안녕 __XT_PH_9999__\n", "안녕")]
    public async Task SingleRow_KeepsTheSourceEdgeWhitespace(string source, string modelOutput, string expected)
    {
        await using var fixture = await TranslationRunFixture.CreateAsync((source, "MESG:DESC"));
        fixture.Client.ResponseOverride = (_, _) => modelOutput;

        await fixture.Service.TranslateIdsAsync(fixture.Request with { TargetLang = "korean" });

        var row = Assert.Single((await fixture.RowsAsync()).Values);
        Assert.Equal(StringEntryStatus.Done, row.Status);
        Assert.Equal(expected, row.DestText);
    }

    // A quoted line comes back quoted, often without the end sentinel, and was then read as a JSON string literal:
    // "\"Never again.\"" was saved without its quotes. An answer the model put in JSON quotes on its own is still unwrapped.
    [Theory]
    [InlineData("\"Never again.\"", "\"다시는 안 돼.\"", "\"다시는 안 돼.\"")]
    [InlineData("“Never again.”", "\"다시는 안 돼.\"", "\"다시는 안 돼.\"")]
    [InlineData("\"Never again.\"", "\"다시는 안 돼.\" __XT_PH_9999__", "\"다시는 안 돼.\"")]
    [InlineData("Never again.", "\"다시는 안 돼.\"", "다시는 안 돼.")]
    public async Task SingleRow_UnwrapsAJsonStringOnlyWhenTheSourceIsNotQuoted(string source, string modelOutput, string expected)
    {
        await using var fixture = await TranslationRunFixture.CreateAsync((source, "INFO:NAM1"));
        fixture.Client.ResponseOverride = (_, _) => modelOutput;

        await fixture.Service.TranslateIdsAsync(fixture.Request with { TargetLang = "korean" });

        var row = Assert.Single((await fixture.RowsAsync()).Values);
        Assert.Equal(StringEntryStatus.Done, row.Status);
        Assert.Equal(expected, row.DestText);
    }

    // Most short rows go through the batch path, which saved "Gold: " as "골드:" (only the one-row path restored it).
    [Fact]
    public async Task Batch_KeepsTheSourceEdgeWhitespace()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Gold: ", "MESG:DESC"), ("  Silver", "MESG:DESC"));
        var ids = fixture.Ids;
        fixture.Client.ResponseOverride = (_, _) =>
            "{\"translations\":[{\"id\":" + ids[0] + ",\"text\":\"골드:\"},{\"id\":" + ids[1] + ",\"text\":\"은\"}]}";

        await fixture.Service.TranslateIdsAsync(fixture.Request with { TargetLang = "korean", BatchSize = 2 });

        var rows = await fixture.RowsAsync();
        Assert.Equal(("골드: ", StringEntryStatus.Done), (rows[ids[0]].DestText, rows[ids[0]].Status));
        Assert.Equal("  은", rows[ids[1]].DestText);
    }

    // The final text has its line breaks back: one at the edge of a joined title is the translation's own, and
    // trimming it with the spaces saved the line with no break.
    [Fact]
    public async Task Batch_KeepsALineBreakTheTranslationEndsIn()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("A Short History\nof Morrowind", "MESG:DESC"), ("Gold: ", "MESG:DESC"));
        var ids = fixture.Ids;
        fixture.Client.ResponseOverride = (_, _) =>
            "{\"translations\":[{\"id\":" + ids[0] + ",\"text\":\"모로윈드 약사__XT_PH_0000__\"},{\"id\":" + ids[1] + ",\"text\":\"골드:\"}]}";

        await fixture.Service.TranslateIdsAsync(fixture.Request with { TargetLang = "korean", BatchSize = 2 });

        var rows = await fixture.RowsAsync();
        Assert.Equal(("모로윈드 약사\n", StringEntryStatus.Done), (rows[ids[0]].DestText, rows[ids[0]].Status));
    }

    [Theory]
    [InlineData("Gold: ", "골드:", "골드: ")]
    [InlineData("  Silver", " 은 ", "  은")]
    [InlineData("A Short History\nof Morrowind", "모로윈드 약사\n", "모로윈드 약사\n")]
    [InlineData("\nHello world", "\n안녕\n세상", "\n안녕\n세상")]
    [InlineData("Line one\n ", "첫 줄\n", "첫 줄\n ")]
    public void EdgeSpaces_AreRestored_AndLineBreaksKept(string source, string translated, string expected)
        => Assert.Equal(expected, TranslationService.RestoreSourceEdgeSpaces(source, translated));
}
