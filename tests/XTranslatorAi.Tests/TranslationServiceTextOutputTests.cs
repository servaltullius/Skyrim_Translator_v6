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
        await using var fixture = await TranslationRunFixture.CreateAsync(("A Short History\nof Morrowind", "MESG:DESC"), ("Gold: ", "MESG:DESC"),
            ("A Short History\nof Morrowind", "MESG:DESC"));
        var ids = fixture.Ids;
        fixture.Client.ResponseOverride = (_, _) =>
            "{\"translations\":[{\"id\":" + ids[0] + ",\"text\":\"모로윈드 약사__XT_PH_0000__\"},{\"id\":" + ids[1] + ",\"text\":\"골드:\"}]}";

        await fixture.Service.TranslateIdsAsync(fixture.Request with { TargetLang = "korean", BatchSize = 3 });

        var rows = await fixture.RowsAsync();
        Assert.Equal(("모로윈드 약사\n", StringEntryStatus.Done), (rows[ids[0]].DestText, rows[ids[0]].Status));
        // The same text in another row is filled from the first (the duplicate-row path).
        Assert.Equal(("모로윈드 약사\n", StringEntryStatus.Done), (rows[ids[2]].DestText, rows[ids[2]].Status));
    }

    private static async Task SetEdidsAsync(TranslationRunFixture fixture, params string[] edids)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={fixture.Db.DbPath};Pooling=False");
        await connection.OpenAsync();
        for (var i = 0; i < edids.Length; i++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE StringEntry SET Edid = $edid WHERE Id = $id";
            command.Parameters.AddWithValue("$edid", edids[i]);
            command.Parameters.AddWithValue("$id", fixture.Ids[i]);
            await command.ExecuteNonQueryAsync();
        }
    }

    // One risky sentence among a batch's rows had the whole batch asked for three candidates (three times the output).
    // It goes with the rows of its group (a skill's names), which are translated in one request.
    [Fact]
    public async Task RiskySentence_IsAskedForCandidatesInABatchOfItsOwn()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Iron Sword", "WEAP:FULL"), ("Protects you from fire.", "MGEF:DNAM"),
            ("Steel Sword", "WEAP:FULL"), ("Learning - Flame of Lion", "BOOK:FULL"), ("Rim Scroll - Flame of Lion", "SCRL:FULL"),
            ("Flame of Lion", "SPEL:FULL"), ("Hurls flame against your foes.", "SPEL:DESC"));
        await SetEdidsAsync(fixture, "WeapIron", "MgefFireWard", "WeapSteel", "BookFlame", "ScrollFlame", "SpelFlame", "SpelFlame");

        await fixture.Service.TranslateIdsAsync(fixture.Request with { TargetLang = "korean", BatchSize = 12, EnableRiskyCandidateRerank = true });

        var prompts = fixture.Client.Requests.Where(r => r.GenerationConfig?.CandidateCount > 1).Select(r => r.Contents[0].Parts[0].Text!).ToList();
        Assert.NotEmpty(prompts);
        Assert.All(prompts, prompt => Assert.DoesNotContain("Iron Sword", prompt, StringComparison.Ordinal));
        Assert.Contains(prompts, prompt => prompt.Contains("Protects you from fire", StringComparison.Ordinal));
        var flame = Assert.Single(prompts, prompt => prompt.Contains("against your foes", StringComparison.Ordinal));
        Assert.Contains("Learning - Flame of Lion", flame, StringComparison.Ordinal);
        Assert.Contains("Rim Scroll - Flame of Lion", flame, StringComparison.Ordinal);
    }

    // A key switch (failover on a 429) stopped one worker, and the others went on taking batches with the same key.
    [Fact]
    public async Task KeySwitch_StopsTheOtherWorkersTakingBatches()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            Enumerable.Range(1, 8).Select(i => ($"Iron Sword {i}", "WEAP:FULL")).ToArray());
        fixture.Client.BeforeGenerate = async (call, _, ct) =>
        {
            if (call == 1) throw new GeminiHttpException("generateContent", 429, "Too Many Requests", null, "HTTP 429 quota exceeded");
            await Task.Delay(300, ct);
            // The other worker's request in flight ends on an error of its own after the stop.
            if (call == 2) throw new GeminiHttpException("generateContent", 503, "Service Unavailable", null, "HTTP 503 overloaded");
        };

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.TranslateIdsAsync(fixture.Request with { MaxConcurrency = 2, EnableApiKeyFailover = true }));

        Assert.True(fixture.Client.Calls <= 3, $"calls: {fixture.Client.Calls}");
        Assert.DoesNotContain((await fixture.RowsAsync()).Values, row => row.Status == StringEntryStatus.Error);
    }

    // A row the preparation cannot take (more than 9,999 protected pieces) or a row deleted since stopped the whole run.
    [Fact]
    public async Task RowThePreparationCannotTake_IsThatRowsError()
    {
        var huge = "Hello " + string.Join(" ", Enumerable.Repeat("%s", 10_000));
        await using var fixture = await TranslationRunFixture.CreateAsync((huge, "MESG:DESC"), ("Iron Sword", "WEAP:FULL"));

        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = fixture.Ids.Append(999_999).ToArray() });

        var rows = await fixture.RowsAsync();
        Assert.Equal(StringEntryStatus.Error, rows[fixture.Ids[0]].Status);
        Assert.Contains("Too many placeholders", rows[fixture.Ids[0]].ErrorMessage);
        Assert.Equal("E331", XTranslatorAi.Core.Diagnostics.UserFacingErrorClassifier.ClassifyErrorMessage(rows[fixture.Ids[0]].ErrorMessage).Code);
        Assert.Equal(StringEntryStatus.Done, rows[fixture.Ids[1]].Status);
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
