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
        // The translation is kept as it is: a mark holds while the row's text is the same, or reads the same way.
        Assert.True(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", " 안녕, 소린. "));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", "안녕하세요, 소린."));
        Assert.False(LqaReviewedMarks.Holds(marks, sword, "english_residue", "철 검"));
        Assert.False(LqaReviewedMarks.Holds(marks, sword, "token_mismatch", "Iron 검"));
        // A speech-level mark holds when an update moves the line to another speech-level check; not for another concern.
        Assert.True(LqaReviewedMarks.Holds(marks, line, "speaker_tone_mixed", " 안녕, 소린. "));
        Assert.True(LqaReviewedMarks.Holds(marks, line, "player_tone_inconsistent", " 안녕, 소린. "));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "speaker_tone_mixed", "안녕하세요, 소린."));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "glossary_missing", " 안녕, 소린. "));

        await fixture.Db.SetLqaReviewedAsync(new[] { (line, "english_residue", "") }, false, CancellationToken.None);

        Assert.Equal(new[] { (line, "speaker_tone_inconsistent"), (sword, "english_residue") }.ToHashSet(),
            (await fixture.Db.GetLqaReviewedMarksAsync(CancellationToken.None)).Keys.Select(k => (k.StringId, k.Code)).ToHashSet());
    }

    /// <summary>
    /// Polishing Serana Dialogue Add-On brought back marks on lines that were only reworded: her 존댓말 question to the
    /// court mage stayed 존댓말, and the result was looked at again for nothing. A speech-level mark holds while the
    /// line reads at the level it was marked at (read by the check that raised the result), and an English-residue
    /// mark while the line has no Latin word the marked text did not; other marks still want the same text.
    /// </summary>
    [Fact]
    public void Marks_HoldThroughARewording_ThatKeepsWhatWasJudged()
    {
        const long line = 1, lorem = 2, name = 3;
        var marks = new Dictionary<(long StringId, string Code), string>
        {
            [(line, "speaker_tone_inconsistent")] = "시빌, 토리그와는 알고 지낸 지 얼마나 됐나요? 그를 잘 알았나요?",
            [(lorem, "english_residue")] = "세라나가 장난칠 시간이야! Lorem ipsum dolor domet.",
            [(name, "glossary_missing")] = "도대체 오블리비언의 이름으로 뭘 원하는 거야?",
        };

        Assert.True(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", "시빌, 토리그와는 알고 지낸 지 얼마나 됐나요? 잘 아는 사이였나요?"));
        // Another speech-level check reads the reworded line with its own levels; both texts are still 존댓말.
        Assert.True(LqaReviewedMarks.Holds(marks, line, "tone_inconsistent", "시빌, 토리그와는 알고 지낸 지 얼마나 됐나요? 잘 아는 사이였나요?"));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", "시빌, 토리그와는 알고 지낸 지 얼마나 됐어? 잘 아는 사이였어?"));
        Assert.False(LqaReviewedMarks.Holds(marks, line, "speaker_tone_inconsistent", "시빌, 토리그와는 알고 지낸 지 얼마나 됐소? 잘 아는 사이였소?"));

        Assert.True(LqaReviewedMarks.Holds(marks, lorem, "english_residue", "세라나가 장난칠 시간! Lorem ipsum dolor domet."));
        Assert.False(LqaReviewedMarks.Holds(marks, lorem, "english_residue", "세라나가 장난칠 시간! Lorem ipsum dolor domet, pee poo."));

        Assert.False(LqaReviewedMarks.Holds(marks, name, "glossary_missing", "도대체 뭘 원하는 거야?"));
    }

    /// <summary>
    /// A speech-level result held by another check's mark on its line could not be taken off: unmarking removed its own
    /// code, which had no mark, and the other mark kept it 확인함. Taking a mark off removes every code that holds it.
    /// </summary>
    [Fact]
    public void TakingAMarkOff_RemovesTheMarksThatHoldIt()
    {
        const string text = "시빌, 토리그와는 알고 지낸 지 얼마나 됐나요?";
        var marks = new Dictionary<(long StringId, string Code), string> { [(1, "speaker_tone_inconsistent")] = text, [(1, "english_residue")] = text };
        Assert.True(LqaReviewedMarks.Holds(marks, 1, "tone_inconsistent", text));

        foreach (var code in LqaReviewedMarks.CodesHolding("tone_inconsistent")) marks.Remove((1, code));

        Assert.False(LqaReviewedMarks.Holds(marks, 1, "tone_inconsistent", text));
        Assert.False(LqaReviewedMarks.Holds(marks, 1, "speaker_tone_inconsistent", text));
        Assert.True(LqaReviewedMarks.Holds(marks, 1, "english_residue", text));
        Assert.Equal(new[] { "glossary_missing" }, LqaReviewedMarks.CodesHolding("glossary_missing"));
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
