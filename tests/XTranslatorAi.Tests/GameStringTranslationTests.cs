using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Rows the game itself translates (a vanilla line a mod overrides, or a vanilla book text) take the player's game
/// translation without a request, with a note saying where it came from; the others are still translated.
/// </summary>
public class GameStringTranslationTests
{
    [Fact]
    public async Task RowsWithAGameTranslation_TakeIt_WithoutARequest()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("What do you want?", "INFO:NAM1"), ("Iron Sword", "WEAP:FULL"));
        var rows = await fixture.RowsAsync();
        var line = rows.Values.Single(r => r.SourceText == "What do you want?");

        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GameStringMatches = new Dictionary<long, GameStringMatch> { [line.Id] = new("뭐가 필요합니까?", "Skyrim.esm 같은 레코드") },
        });

        var after = await fixture.RowsAsync();
        Assert.Equal("뭐가 필요합니까?", after[line.Id].DestText);
        Assert.Equal(StringEntryStatus.Done, after[line.Id].Status);
        var sent = string.Join(" | ", fixture.Client.Requests.Select(r => r.Contents[0].Parts[0].Text));
        Assert.DoesNotContain("What do you want?", sent);
        Assert.Contains("Iron Sword", sent);
        var notes = await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.GameStringNoteKind, CancellationToken.None);
        Assert.Contains("Skyrim.esm 같은 레코드", notes[line.Id]);
    }

    // A game text whose tags no longer fit the row (a mod changed the markup) is not forced; the model translates it.
    [Fact]
    public async Task GameTranslationThatBreaksTheRowsTags_IsLeftToTheModel()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Deals <mag> damage.", "SPEL:DESC"));
        var row = (await fixture.RowsAsync()).Values.Single();

        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GameStringMatches = new Dictionary<long, GameStringMatch> { [row.Id] = new("피해를 줍니다.", "같은 문장") },
        });

        Assert.NotEmpty(fixture.Client.Requests);
    }

    // A base game book the mod copied with changes is translated, with the Korean patch's text in its request.
    [Fact]
    public async Task EditedCopyOfABaseGameBook_IsTranslatedWithTheKoreanPatchTextAsReference()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("They overthrew the dwarves, and fled.", "BOOK:DESC"));
        var row = (await fixture.RowsAsync()).Values.Single();

        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GameStringReferences = new Dictionary<long, GameStringMatch>
            {
                [row.Id] = new("<p>그들은 드워프를 무너뜨렸다.</p>\r\n\r\n크래그 전쟁", "Skyrim.esm 비슷한 글 3/4문단",
                    new[] { "They overthrew the dwarves, and fled." }),
            },
        });

        var sent = string.Join(" | ", fixture.Client.Requests.Select(r => r.Contents[0].Parts[0].Text));
        Assert.Contains("Korean patch text:", sent);
        Assert.Contains("Copy the Korean patch text paragraph by paragraph", sent);
        Assert.Contains("- They overthrew the dwarves, and fled.", sent);
        Assert.Contains("그들은 드워프를 무너뜨렸다.", sent);
        Assert.DoesNotContain("<p>그들은", sent);
    }

    /// <summary>
    /// The same base game book in two records (LotD keeps several): the second is saved from the first one's answer,
    /// and it takes the Korean patch's blocks too instead of the model's text alone.
    /// </summary>
    [Fact]
    public async Task DuplicateOfAnEditedBaseGameBook_IsSplicedLikeTheFirst()
    {
        const string english = "The first paragraph of the base game book, long enough to be counted.\r\n\r\n"
                               + "The second paragraph of the base game book, also long enough to count.\r\n\r\n"
                               + "The third paragraph of the base game book, which the mod left alone too.\r\n\r\n"
                               + "The fourth paragraph of the base game book, which the mod rewrote a little.";
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", new string[0], new[]
            {
                new PluginField("BOOK/00001000/0/DESC/0", 0, "BOOK", "DESC", 0x1000, null, 0, 0, english, PluginStringTableKind.DlStrings, 1),
            }, new Dictionary<(PluginStringTableKind, uint), string>
            {
                [(PluginStringTableKind.DlStrings, 1)] = "바닐라 책의 첫 문단.\r\n\r\n바닐라 책의 둘째 문단.\r\n\r\n바닐라 책의 셋째 문단.\r\n\r\n바닐라 책의 넷째 문단.",
            }),
        });
        var copy = english.Replace("rewrote a little.", "rewrote a little more.");
        await using var fixture = await TranslationRunFixture.CreateAsync((copy, "BOOK:DESC"), (copy, "BOOK:DESC"));
        var match = game.FindSimilar("BOOK:DESC", copy);
        Assert.NotNull(match);

        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            GameStringReferences = fixture.Ids.ToDictionary(id => id, _ => match),
        });

        var rows = await fixture.RowsAsync();
        Assert.All(rows.Values, row =>
        {
            Assert.Contains("바닐라 책의 첫 문단.", row.DestText);
            Assert.Contains("바닐라 책의 셋째 문단.", row.DestText);
            Assert.DoesNotContain("바닐라 책의 넷째 문단.", row.DestText);
        });
    }
}
