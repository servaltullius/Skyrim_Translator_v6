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

    /// <summary>
    /// Fists of Fury: Reggr's one 존댓말 line among few 반말 ones had no clear majority to warn by and passed. A speaker
    /// with too few lines, or split down the middle, is listed for reference: the odd side, or every line in a tie.
    /// </summary>
    [Fact]
    public async Task FewOrSplitLines_AreListedForReference()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "훌륭했어!", speaker: "0A000001"),
            Line(2, "다음엔 더 세게 덤벼 봐.", speaker: "0A000001"),
            Line(3, "여기 있어요, 당신 몫입니다.", speaker: "0A000001"),
            Line(4, "어서 오세요.", speaker: "0A000002"),
            Line(5, "행운을 빌어요.", speaker: "0A000002"),
            Line(6, "나중에 다시 와.", speaker: "0A000002"),
            Line(7, "돈만 있으면 돼.", speaker: "0A000002"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code == "speaker_tone_inconsistent");
        var mixed = issues.Where(i => i.Code == "speaker_tone_mixed").ToList();
        Assert.Equal(new long[] { 3, 4, 5, 6, 7 }, mixed.Select(i => i.Id).OrderBy(id => id));
        Assert.All(mixed, i => Assert.Equal("Info", i.Severity));
        Assert.Contains("존댓말 1줄, 반말 2줄, 이 줄 존댓말", mixed.Single(i => i.Id == 3).Message);
    }

    /// <summary>
    /// The player's level follows who is spoken to: the choices to one NPC keep one level (the odd one is listed for
    /// reference), and choices to another NPC may be in another level.
    /// </summary>
    [Fact]
    public async Task PlayerLines_KeepOneLevelPerListener()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "당신이 주최자인가요?", speaker: "0A000002", rec: "DIAL:FULL"),
            Line(2, "참가하러 왔어요.", speaker: "0A000002", rec: "DIAL:FULL"),
            Line(3, "보상을 받고 싶어.", speaker: "0A000002", rec: "INFO:RNAM"),
            Line(4, "가르쳐 줄 수 있어?", speaker: "0A000003", rec: "INFO:RNAM"),
            Line(5, "싸운 지 얼마나 됐어?", speaker: "0A000003", rec: "DIAL:FULL"),
            Line(6, "어서 오세요.", speaker: "0A000002"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var player = Assert.Single(issues, i => i.Code == "player_tone_inconsistent");
        Assert.Equal(3, player.Id);
        Assert.Equal("Info", player.Severity);
        Assert.DoesNotContain(issues, i => i.Code is "speaker_tone_inconsistent" or "speaker_tone_mixed");
    }
    /// <summary>
    /// The review of 77afece: a polite "네", nouns ending like 반말 (가게, 그런데, 노래) and a retold quotation were
    /// judged the wrong level, and a speech-check choice, meant to differ, was compared with the player's others.
    /// </summary>
    [Fact]
    public async Task YesNounsQuotesAndSpeechChecks_AreNotMisjudged()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다. 저는 탈모어 대사입니다."),
            Line(2, "그 이름은 익숙하리라 믿습니다."),
            Line(3, "당신의 협조는 기억해 두겠습니다."),
            Line(4, "참으로 흥미롭군요."),
            Line(5, "음... 네."),
            Line(6, "여긴 내 가게."),
            Line(7, "그런데..."),
            Line(8, "\"꺼져!\" 하고 소리치더군요."),
            Line(9, "\"살려 주세요!\" 하고 울부짖더군.", speaker: "0A000009"),
            Line(10, "다들 겁에 질렸어.", speaker: "0A000009"),
            Line(11, "모두 도망쳤어.", speaker: "0A000009"),
            Line(12, "당신이 주최자인가요?", speaker: "0A000002", rec: "DIAL:FULL"),
            Line(13, "참가하러 왔어요.", speaker: "0A000002", rec: "DIAL:FULL"),
            new(14, 14, null, "DIAL:FULL", StringEntryStatus.Done, "Hand it over. (Intimidate)", "내놔. (위협)", Speaker: "0A000002"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code is "speaker_tone_inconsistent" or "speaker_tone_mixed" or "player_tone_inconsistent");
    }
}
