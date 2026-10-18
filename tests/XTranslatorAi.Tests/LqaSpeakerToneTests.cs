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
    /// IDE Thalmor's scenes: Elenwen is polite to General Tullius and curt with her agents. Her scene lines to one NPC
    /// ("speaker>listener") are compared among themselves, apart from her lines to the player.
    /// </summary>
    [Fact]
    public async Task SceneLinesToAnotherNpc_AreComparedByThePair()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."),
            Line(2, "익숙하리라 믿습니다."),
            Line(3, "기억해 두겠습니다."),
            Line(4, "물러가십시오."),
            // To an agent, curt as she is with all of them: not compared with her lines to the player.
            Line(5, "그를 잘 지켜보고, 성과를 내게 해.", speaker: "00013269>01000801"),
            Line(6, "툴리우스 장군, 언제 뵈어도 반갑습니다.", speaker: "00013269>0001327E"),
            Line(7, "제가 듣기로 이단 신앙을 품고 있을지도 모른다더군요.", speaker: "00013269>0001327E"),
            Line(8, "철저한 조사를 위해 군단의 협조를 요청합니다.", speaker: "00013269>0001327E"),
            Line(9, "좀 더 협조적인 길도 있었다는 걸 생각해 보십시오.", speaker: "00013269>0001327E"),
            Line(10, "네 대답이 마음에 드는군. 내가 기대한 답이다.", speaker: "00013269>0001327E"),
            // To agents the plugin does not name (Actor1, Actor2): not the player either.
            Line(11, "윈터홀드 상황은 어떻게 되어 가지?", speaker: "00013269>00000000"),
            Line(12, "내일 새벽 윈터홀드로 가서 그의 우선순위를 바로잡아라.", speaker: "00013269>00000000"),
            Line(13, "임페리얼은 그들끼리 모험을 즐기게 둬.", speaker: "00013269>00000000"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code is "speaker_tone_inconsistent" or "speaker_tone_mixed");
        Assert.Equal(("speaker_tone_inconsistent", 10L), (issue.Code, issue.Id));
        Assert.Contains("같은 화자가 같은 상대에게 한 다른 대사와 말투가 다릅니다", issue.Message);
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

    /// <summary>
    /// The review of Serana Dialogue Add-On: a book's line read out in single quotes, a creature's "포요! 포요!" between
    /// her sentences and a stage direction between asterisks made 반말 lines count as 존댓말. An apostrophe in a name
    /// opens no quotation, and a polite line with a stage direction is still polite.
    /// </summary>
    [Fact]
    public async Task SingleQuotesCriesAndStageDirections_AreNotTheSpeakersLevel()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "응, 실은 그래."),
            Line(2, "그 말을 들으니 기쁘네. 잘됐어."),
            Line(3, "마음만 먹으면 여길 전부 무너뜨릴 수도 있어."),
            Line(4, "빨리 필요한 거 챙겨 와."),
            Line(9, "다음엔 더 세게 덤벼 봐."),
            Line(10, "괜찮아."),
            Line(5, "아, 속편이네. '내 창을 좀 닦아 주시겠어요, 내 사랑? 좀... 녹슬었거든요.'"),
            Line(6, "*움브라* 이제는 어때? 들려? 난 이제 말하는 검이야, *그 모드에 나오는 버섯* 포요! 포요! *질척* 그리고 난 조그만 드래곤이야!"),
            Line(7, "J'zargo가 그러는데, 그 녀석 마법은 대단하대요. 정말 놀라워요."),
            Line(8, "*웃음* 그러게요."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 7, 8 }, issues.Where(i => i.Code == "speaker_tone_inconsistent").Select(i => i.Id).OrderBy(id => id));
    }

    /// <summary>
    /// The review of the fix above: a real short 해요체 reply said once, the 요 left after a quotation, a reply left
    /// after a stage direction and the second of two names with apostrophes are still the speaker's level; only a
    /// repeated cry and a quoted book line are not. Each line is judged among four lines of the other level.
    /// </summary>
    [Theory]
    [InlineData("뭐요? 그게 무슨 뜻이오?", false, true)]
    [InlineData("저요! 저!", false, true)]
    [InlineData("네, 가요. 지금 당장.", false, true)]
    [InlineData("'용의 피'요? 그건 좀...", false, true)]
    [InlineData("J'zargo는 모르겠대요. 하지만 M'aiq가 그랬어.", false, true)]
    [InlineData("지'자르고가 그래요. 지'자르고는 최고야.", false, true)]
    [InlineData("*미소* 뭐죠?", false, true)]
    [InlineData("*한숨* 좋아, 시작할게.", true, true)]
    [InlineData("*고개를 끄덕인다* 됐어.", true, true)]
    [InlineData("가요! 가요! 빨리!", false, true)]
    [InlineData("봐요! 봐요! 진짜 드래곤이다!", false, true)]
    [InlineData("웃기는 소리군. 읽을 줄 아는 사람은 문학의 '학자가 아니다'?", true, true)]
    [InlineData("\"살려 줘!\" 어?", true, false)]
    [InlineData("야를님, 이걸 어떻게 말씀드려야 할지...", true, false)]
    [InlineData("그게 어떻게 될지 누가 알겠어.", true, true)]
    [InlineData("누가 알아, 어떻게 될지.", true, true)]
    [InlineData("넌 알잖아, 무슨 일이 있었는지.", true, true)]
    [InlineData("모든 상자를 살펴보라고. 누가 알겠어. 그 안에 뭔가 비싼 게 들어있을지.", true, true)]
    [InlineData("잘 있어, 엔써... 뭐라 감사의 말을 해야 할지...", true, true)]
    [InlineData("도저히 믿을 수가 없군.", true, true)]
    [InlineData("와아... *숨을 헐떡이며*", true, false)]
    [InlineData("아, 주인님. *절하며*", true, false)]
    [InlineData("포요! 포요! 난 조그만 드래곤이야!", false, false)]
    [InlineData("*움브라* 난 이제 말하는 검이야, *그 모드에 나오는 버섯* 포요! 포요! *질척* 그리고 난 조그만 드래곤이고!", false, false)]
    [InlineData("아, 속편이네. '내 창을 좀 닦아 주시겠어요, 내 사랑? 좀... 녹슬었거든요.'", false, false)]
    public async Task OneLineAmongTheOtherLevel_IsJudgedByWhatTheSpeakerSays(string line, bool politeSpeaker, bool flagged)
    {
        var others = politeSpeaker
            ? new[] { "환영합니다.", "익숙하리라 믿습니다.", "기억해 두겠습니다.", "이제 물러가십시오." }
            : new[] { "그 말을 들으니 기쁘네. 잘됐어.", "마음만 먹으면 여길 전부 무너뜨릴 수도 있어.", "빨리 필요한 거 챙겨 와.", "다음엔 더 세게 덤벼 봐." };
        var entries = others.Select((text, i) => Line(i + 1, text)).Append(Line(9, line)).ToList();

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var warned = issues.Where(i => i.Code == "speaker_tone_inconsistent").Select(i => i.Id).ToList();
        Assert.Equal(flagged ? new long[] { 9 } : Array.Empty<long>(), warned);
    }
}
