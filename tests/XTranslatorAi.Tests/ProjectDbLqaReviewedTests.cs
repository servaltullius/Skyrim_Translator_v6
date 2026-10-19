using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// Serana Dialogue Add-On's 16 warnings left as they are on purpose (Serana speaking politely to her father, a mod
/// name kept in English) came back on every quality check among the ones still to fix. A "확인함" mark hides a
/// result while the row's translation is the one it was marked with.
/// </summary>
public sealed class ProjectDbLqaReviewedTests
{
    [Fact]
    public async Task Marks_AreKeptByRowAndCode_WithTheTranslation_AndCanBeTakenOff()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Hey Sorine.", "INFO:NAM1"), ("Iron Sword", "WEAP:FULL"));
        var (line, sword) = (fixture.Ids[0], fixture.Ids[1]);

        await fixture.Db.SetLqaReviewedAsync(new[]
        {
            (line, "speaker_tone_inconsistent", " 안녕, 소린. "),
            (line, "english_residue", " 안녕, 소린. "),
            (sword, "english_residue", "Iron 검"),
        }, true, CancellationToken.None);

        var marks = await fixture.Db.GetLqaReviewedMarksAsync(CancellationToken.None);
        Assert.Equal(3, marks.Count);
        // The translation is kept as it is: a mark holds only while the row's text is the same.
        Assert.True(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", " 안녕, 소린. "));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", "안녕, 소린."));
        Assert.False(LqaReviewedMarks.Holds(marks, sword, "english_residue", "철 검"));
        Assert.False(LqaReviewedMarks.Holds(marks, sword, "token_mismatch", "Iron 검"));
        // A speech-level mark holds when an update moves the line to another speech-level check; not for another concern.
        Assert.True(LqaReviewedMarks.Holds(marks, line, "speaker_tone_mixed", " 안녕, 소린. "));
        Assert.True(LqaReviewedMarks.Holds(marks, line, "player_tone_inconsistent", " 안녕, 소린. "));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "speaker_tone_mixed", "안녕, 소린."));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "glossary_missing", " 안녕, 소린. "));

        await fixture.Db.SetLqaReviewedAsync(new[] { (line, "english_residue", "") }, false, CancellationToken.None);

        Assert.Equal(new[] { (line, "speaker_tone_inconsistent"), (sword, "english_residue") }.ToHashSet(),
            (await fixture.Db.GetLqaReviewedMarksAsync(CancellationToken.None)).Keys.Select(k => (k.StringId, k.Code)).ToHashSet());
    }

    // A retranslated row is checked afresh.
    [Fact]
    public async Task Retranslating_TakesTheRowsMarksOff()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Hey Sorine.", "INFO:NAM1"), ("Iron Sword", "WEAP:FULL"));
        var (line, sword) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.UpdateStringTranslationAsync(line, "안녕하세요, 소린.", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(sword, "Iron 검", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.SetLqaReviewedAsync(new[] { (line, "speaker_tone_inconsistent", "안녕하세요, 소린."), (sword, "english_residue", "Iron 검") },
            true, CancellationToken.None);

        await fixture.Db.ResetForRetranslationAsync(new[] { line }, includeEdited: false, CancellationToken.None);

        Assert.Equal(new[] { sword }, (await fixture.Db.GetLqaReviewedMarksAsync(CancellationToken.None)).Keys.Select(k => k.StringId));
    }
}
