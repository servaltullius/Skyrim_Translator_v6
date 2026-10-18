using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// IDE Thalmor: Ondolemar spoke 하오, 해라, 합니다 and 해요 from one topic to the next, and the dialogue-group check,
/// which compares one topic's lines, raised three warnings in all. One speaker's lines are now compared across the
/// plugin, at the level that matters (존댓말 or 반말).
/// </summary>
public class LqaSpeakerToneTests
{
    private static LqaScanEntry Line(long id, string dest, string? speaker = "00013269", string rec = "INFO:NAM1")
        => new(id, (int)id, null, rec, StringEntryStatus.Done, "English line.", dest, Speaker: speaker);

    [Fact]
    public async Task SpeakerLine_InTheOtherLevel_IsFlagged()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다. 저는 탈모어 대사입니다."),
            Line(2, "그 이름은 익숙하리라 믿습니다."),
            Line(3, "당신의 협조는 기억해 두겠습니다."),
            Line(4, "참으로 흥미롭군요."),
            Line(5, "이제 물러가십시오."),
            Line(6, "시민, 이 대화는 끝났다."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "speaker_tone_inconsistent");
        Assert.Equal(6, issue.Id);
        Assert.Contains("대부분 존댓말, 이 대사 반말", issue.Message);
    }

    /// <summary>합니다체 and 해요체 are both 존댓말: "~습니다" and "~군요" mix naturally in one speaker's polite lines.</summary>
    [Fact]
    public async Task HamnidaAndHaeyo_AreOneLevel()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."),
            Line(2, "익숙하리라 믿습니다."),
            Line(3, "참으로 흥미롭군요."),
            Line(4, "실용적이었지요."),
            Line(5, "물러가십시오."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code == "speaker_tone_inconsistent");
    }

    /// <summary>Other speakers, lines with no known speaker and the player's choices are not counted.</summary>
    [Fact]
    public async Task OtherSpeakersAndPlayerChoices_AreNotCompared()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."),
            Line(2, "익숙하리라 믿습니다."),
            Line(3, "기억해 두겠습니다."),
            Line(4, "물러가십시오."),
            Line(5, "당장 꺼지라고 했지."),
            Line(6, "저리 가, 귀찮아.", speaker: "0001E7D7"),
            Line(7, "저리 가, 귀찮아.", speaker: null),
            Line(8, "관둬. 안 도울 거야.", rec: "DIAL:FULL"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 5 }, issues.Where(i => i.Code == "speaker_tone_inconsistent").Select(i => i.Id));
    }
}
