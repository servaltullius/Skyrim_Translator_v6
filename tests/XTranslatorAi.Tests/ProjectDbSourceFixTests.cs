using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// A row marked "원문 오류 수정" is translated against its English on purpose ("Disabe Perfect Parry" → "킬무브
/// 비활성화"). Saving it by hand had made that the project TM's translation of the English, and the project TM wins
/// over the series TM: retranslating another row with the same English, or a new version's row, got "킬무브 비활성화".
/// </summary>
public sealed class ProjectDbSourceFixTests
{
    private const string Lang = "english";

    [Fact]
    public async Task MarkingARow_TakesItsTranslationOutOfTheTm()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Disabe Perfect Parry", "ARMO:DESC"), ("Iron Sword", "WEAP:FULL"));
        var (fix, other) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.UpdateStringTranslationAsync(fix, "킬무브 비활성화", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(other, "철 검", StringEntryStatus.Edited, null, CancellationToken.None);

        await fixture.Db.SetSourceFixAsync(fix, true, CancellationToken.None);

        Assert.Equal(new[] { "Iron Sword" }, (await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None)).Select(e => e.SourceText));
        Assert.Contains(fix, (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None)).Keys);
    }

    // The TM's translation of the English came from another row, not from the marked one; it stays.
    [Fact]
    public async Task MarkingARow_KeepsATmTranslationItDidNotWrite()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Disabe Perfect Parry", "ARMO:DESC"), ("Disabe Perfect Parry", "ARMO:DESC"));
        var (good, fix) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.UpdateStringTranslationAsync(fix, "킬무브 비활성화", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(good, "완벽한 패리 비활성화", StringEntryStatus.Edited, null, CancellationToken.None);

        await fixture.Db.SetSourceFixAsync(fix, true, CancellationToken.None);

        Assert.Equal("완벽한 패리 비활성화", Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None)).DestText);
    }

    // "Curse Dissipate" had the description of "Curse - load" pasted in; the TM entry is still that row's translation.
    [Fact]
    public async Task MarkingARow_KeepsATmTranslationAnUnmarkedRowShares()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("When using medium, cumulative Curse.", "SPEL:DESC"), ("When using medium, cumulative Curse.", "SPEL:DESC"));
        var (curse, dissipate) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.UpdateStringTranslationAsync(curse, "매개체 사용 시 저주가 누적됩니다.", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(dissipate, "매개체 사용 시 저주가 누적됩니다.", StringEntryStatus.Done, null, CancellationToken.None);

        await fixture.Db.SetSourceFixAsync(dissipate, true, CancellationToken.None);

        Assert.Equal("매개체 사용 시 저주가 누적됩니다.", Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None)).DestText);
    }

    [Fact]
    public async Task EditsOfAMarkedRow_StayOutOfTheTm_UntilItIsUnmarked()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Disabe Perfect Parry", "ARMO:DESC"), ("Rim Scroll - Sudden Tornado", "MGEF:FULL"));
        var (one, two) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.SetSourceFixAsync(one, true, CancellationToken.None);
        await fixture.Db.SetSourceFixAsync(two, true, CancellationToken.None);

        await fixture.Db.UpdateStringTranslationAsync(one, "킬무브 비활성화", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationsAsync(new (long, string, StringEntryStatus, string?)[]
        {
            (two, "약한 충격: 비틀거림 (한손)", StringEntryStatus.Edited, null),
        }, CancellationToken.None);

        Assert.Empty(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None));

        await fixture.Db.SetSourceFixAsync(one, false, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(one, "완벽한 패리 비활성화", StringEntryStatus.Edited, null, CancellationToken.None);

        Assert.Equal("완벽한 패리 비활성화", Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None)).DestText);
        Assert.DoesNotContain(one, (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None)).Keys);
    }
}
