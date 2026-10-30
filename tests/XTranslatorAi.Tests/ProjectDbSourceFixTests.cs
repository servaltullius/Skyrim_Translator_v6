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
        // Only a translated row is marked (MarkingARowNotTranslated_…).
        await fixture.Db.UpdateStringTranslationsAsync(new (long, string, StringEntryStatus, string?)[]
        {
            (one, "완벽한 패리 비활성화 끔", StringEntryStatus.Done, null), (two, "갑작스러운 회오리", StringEntryStatus.Done, null),
        }, CancellationToken.None);
        Assert.True(await fixture.Db.SetSourceFixAsync(one, true, CancellationToken.None));
        Assert.True(await fixture.Db.SetSourceFixAsync(two, true, CancellationToken.None));

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

    /// <summary>
    /// A 대기 or 오류 row marked kept the mark when the model's translation was saved, and the check skipped a translation
    /// that follows the English. Only a translated row is marked: 완료, 직접 수정, or an XML's row that came with its
    /// translation (건너뜀), but not a held one.
    /// </summary>
    [Fact]
    public async Task MarkingARowNotTranslated_IsRefused()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("Disabe Perfect Parry", "ARMO:DESC"), ("Iron Sword", "WEAP:FULL"), ("Steel Sword", "WEAP:FULL"), ("Elven Sword", "WEAP:FULL"));
        var (pending, error, fromXml, held) = (fixture.Ids[0], fixture.Ids[1], fixture.Ids[2], fixture.Ids[3]);
        await fixture.Db.UpdateStringTranslationAsync(error, "", StringEntryStatus.Error, "오류", CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(fromXml, "강철 검", StringEntryStatus.Skipped, null, CancellationToken.None);
        // A held row keeps the text its field had (an XML's untranslated row can have some), and stays in English.
        await fixture.Db.UpdateStringTranslationAsync(held, "Elven Sword", StringEntryStatus.Pending, null, CancellationToken.None);
        await fixture.Db.HoldStringsAsync(new[] { (held, "규칙상 번역할 수 없음") }, CancellationToken.None);

        Assert.False(await fixture.Db.SetSourceFixAsync(pending, true, CancellationToken.None));
        Assert.False(await fixture.Db.SetSourceFixAsync(error, true, CancellationToken.None));
        Assert.False(await fixture.Db.SetSourceFixAsync(held, true, CancellationToken.None));
        Assert.True(await fixture.Db.SetSourceFixAsync(fromXml, true, CancellationToken.None));

        Assert.Equal(new[] { fromXml }, (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None)).Keys);

        // A mark put on a 대기 row before this rule is taken off when the row is marked again.
        await fixture.Db.UpsertStringNoteAsync(pending, TranslationConstants.SourceFixNoteKind, "원문 오류 수정", CancellationToken.None);
        Assert.False(await fixture.Db.SetSourceFixAsync(pending, true, CancellationToken.None));
        Assert.Equal(new[] { fromXml }, (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, CancellationToken.None)).Keys);
    }
}
