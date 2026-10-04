using XTranslatorAi.Core.Models;
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
}
