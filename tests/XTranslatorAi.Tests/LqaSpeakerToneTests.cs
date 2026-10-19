using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
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
    /// <summary>
    /// A scene line whose listener is a guess (the two-actor scene looks at no one) may be said to the player: Meridia's
    /// Order's Alara said "Champion, meet us at Fort Justice." in a scene with Marith. It is left out of both groups'
    /// counts and flagged only when it fits neither her lines to Marith nor her lines to the player.
    /// </summary>
    [Fact]
    public async Task GuessedListener_IsFlaggedOnlyWhenItFitsNeitherGroup()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."), Line(2, "익숙하리라 믿습니다."), Line(3, "기억해 두겠습니다."), Line(4, "물러가십시오."),
            Line(5, "그를 잘 지켜봐.", speaker: "00013269>01000801"), Line(6, "서둘러, 시간이 없어.", speaker: "00013269>01000801"),
            Line(7, "넌 여기 남아.", speaker: "00013269>01000801"), Line(8, "내 말 잘 들어.", speaker: "00013269>01000801"),
            // Either level fits one of the groups.
            Line(9, "챔피언, 저스티스 요새에서 만나요.", speaker: "00013269>01000801?"),
            Line(10, "따라와, 마리스.", speaker: "00013269>01000801?"),
            Line(11, "정말 고맙습니다.", speaker: "00013269>01000801?"),
            // The guessed lines do not tip the pair's group: four 존댓말 here would have made it split.
            Line(12, "부디 조심하십시오.", speaker: "00013269>01000801?"),
            Line(13, "모두 감사합니다.", speaker: "00013269>01000801?"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());
        Assert.DoesNotContain(issues, i => i.Code is "speaker_tone_inconsistent" or "speaker_tone_mixed");

        // To a listener she is polite with as well, a 반말 guess fits neither.
        var polite = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."), Line(2, "익숙하리라 믿습니다."), Line(3, "기억해 두겠습니다."), Line(4, "물러가십시오."),
            Line(5, "장군, 반갑습니다.", speaker: "00013269>0001327E"), Line(6, "협조를 요청합니다.", speaker: "00013269>0001327E"),
            Line(7, "생각해 보십시오.", speaker: "00013269>0001327E"), Line(8, "그렇게 하겠습니다.", speaker: "00013269>0001327E"),
            Line(9, "빨리 따라와.", speaker: "00013269>0001327E?"),
        };
        var issue = Assert.Single(await LqaScanner.ScanAsync(polite, "ko", new List<GlossaryEntry>()), i => i.Code == "speaker_tone_inconsistent");
        Assert.Equal(9L, issue.Id);
        Assert.Contains("상대를 짐작한 대사", issue.Message);
    }

    /// <summary>
    /// Meridia's Order's Laracin was decided to speak 합쇼체 to the Champion, and the translators wrote most of his lines
    /// in 반말: the majority flagged his right lines. A decided level (SpeakerStyles) is what a group is compared with,
    /// for the player's lines to an NPC too.
    /// </summary>
    [Fact]
    public async Task DecidedStyle_IsComparedInsteadOfTheMajority()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "뜻대로 부려라."), Line(2, "이건 믿음이 안 가."), Line(3, "섬멸하자."), Line(4, "얘기하자."),
            Line(5, "명령만 내리십시오."),
            Line(6, "어디로 가지?", speaker: "00013269", rec: "INFO:RNAM"), Line(7, "알겠습니다.", speaker: "00013269", rec: "INFO:RNAM"),
        };
        var styles = new[]
        {
            new SpeakerStyle("00013269", SpeakerStyles.Player, "합쇼체"),
            new SpeakerStyle(SpeakerStyles.PlayerSpeaker, "00013269", "반말"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>(), speakerStyles: styles);

        var flagged = issues.Where(i => i.Code is "speaker_tone_inconsistent" or "player_tone_inconsistent").Select(i => i.Id).OrderBy(i => i);
        Assert.Equal(new long[] { 1, 2, 3, 4, 7 }, flagged);
        Assert.Contains("정한 말투 합쇼체", Assert.Single(issues, i => i.Id == 1).Message);

        var without = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());
        Assert.Contains(without, i => i.Id == 5);
    }

    /// <summary>
    /// A decided 하오체, 하게체 or 반말 is also told apart from the other two, which the side check counts as one:
    /// Teldryn, decided 하오체, kept 반말 and 하게체 lines; ~군, ~지 and ~나? are a 하오체 speaker's too.
    /// </summary>
    [Fact]
    public async Task DecidedHao_NamesLinesInPlainSpeechOrHage()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "여기선 정신 바짝 차려야 해. 그림자 속에 뭐가 숨어 있을지 모르니까."),
            Line(2, "자네 꽤 아파 보이는군."),
            Line(3, "꽤 아파 보이는군. 성소라도 들러 보는 게 어떻소?"),
            Line(4, "오랜만이군, 외지인. 탐리엘에 또 파괴를 불러내러 오셨나?"),
            Line(5, "돈을 낸다면 기꺼이 도와드리지."),
            Line(6, "너 같은 녀석은 처음이야."),
            Line(7, "좋은 생각이오, 조용히 가자."),
            Line(8, "내가 이곳이 싫다고 말했던가?"),
            Line(9, "성자 지웁은 어떻게 되었는지 궁금하군. 그게, 전에 그를 한번 만난 적이 있다오."),
            Line(10, "이제 그만 가 보게나."),
            Line(11, "내 경험에 비추어 말하건대, 농부의 아내들에 대한 소문은 진짜요. 아... 그 달콤했던 여름날."),
            Line(12, "화이트런이라는 이름이면 눈이 더 많아야 하는 거 아니겠소? 허위 광고요, 허위 광고."),
        };
        var styles = new[] { new SpeakerStyle("00013269", SpeakerStyles.Player, "하오체") };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>(), speakerStyles: styles);

        var named = issues.Where(i => i.Code == "speaker_tone_mixed").ToDictionary(i => i.Id, i => i.Message);
        Assert.Equal(new long[] { 1, 2, 6, 10 }, named.Keys.OrderBy(id => id));
        Assert.Contains("이 대사 반말", named[1]);
        Assert.Contains("이 대사 하게체", named[2]);
        Assert.DoesNotContain(issues, i => i.Code == "speaker_tone_inconsistent");
    }

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
    /// 하오체 and 하게체 are on 반말's side, as the Korean patch uses them: a 하오체 voice (Teldryn Sero) mixes "~소" and
    /// "~시오" with "~군", a 하게체 voice (Tolfdir) "~네" and "~게" with "~지". What is warned is 해요체 or 합니다체 in
    /// such a voice and 하오체 in a 존댓말 voice, by name. "~십시오" is still 존댓말, and nouns ending like these
    /// (장소, 미소, 가게, 무게) and the polite "네." have no level.
    /// </summary>
    [Fact]
    public async Task HaoAndHage_AreOnTheCasualSide_AndNamedInTheWarning()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "갈 준비 됐소?", speaker: "0A0000A1"),
            Line(2, "이 기회를 날려버리지 마시오, 외지인. 난 그만한 값어치를 하오.", speaker: "0A0000A1"),
            Line(3, "나와 함께 가고 싶다면, 그들에게 떠나라고 말하시오.", speaker: "0A0000A1"),
            Line(4, "이미 동료가 있는 것 같군.", speaker: "0A0000A1"),
            Line(5, "그건 내가 책임질 거요.", speaker: "0A0000A1"),
            Line(6, "제가 도와드릴게요.", speaker: "0A0000A1"),
            Line(11, "자네 왔나?", speaker: "0A0000B1"),
            Line(12, "이걸 받게.", speaker: "0A0000B1"),
            Line(13, "좋은 생각일세.", speaker: "0A0000B1"),
            Line(14, "그건 사실이지.", speaker: "0A0000B1"),
            Line(15, "마법은 그렇게 쉽게 배우는 게 아니라네.", speaker: "0A0000B1"),
            Line(16, "정말 감사합니다.", speaker: "0A0000B1"),
            Line(21, "환영합니다.", speaker: "0A0000C1"),
            Line(22, "익숙하리라 믿습니다.", speaker: "0A0000C1"),
            Line(23, "이제 물러가십시오.", speaker: "0A0000C1"),
            Line(24, "기억해 두겠어요.", speaker: "0A0000C1"),
            Line(25, "썩 물러가시오.", speaker: "0A0000C1"),
            Line(26, "이곳이 바로 그 장소.", speaker: "0A0000C1"),
            Line(27, "미소.", speaker: "0A0000C1"),
            Line(28, "여긴 내 가게.", speaker: "0A0000C1"),
            Line(29, "엄청난 무게.", speaker: "0A0000C1"),
            Line(30, "네.", speaker: "0A0000C1"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var warned = issues.Where(i => i.Code == "speaker_tone_inconsistent").ToDictionary(i => i.Id, i => i.Message);
        Assert.Equal(new long[] { 6, 16, 25 }, warned.Keys.OrderBy(id => id));
        Assert.Contains("대부분 하오체, 이 대사 존댓말", warned[6]);
        Assert.Contains("대부분 하게체, 이 대사 존댓말", warned[16]);
        Assert.Contains("대부분 존댓말, 이 대사 하오체", warned[25]);
        Assert.DoesNotContain(issues, i => i.Code == "speaker_tone_mixed");
    }

    /// <summary>
    /// The review of the 하오체 change, on the Korean patch: a 하오체 voice's noun + 요 ("어디요?", "멋진 변화요."),
    /// "~ㅂ시다" and "~ㄹ거요" written together were taken for 존댓말 and warned; a 해요체 slip still is. A 반말 voice's
    /// one 하오체 line, warned before for the wrong reason, is listed for reference. The particle 까지 is not 반말.
    /// </summary>
    [Fact]
    public async Task HaoVoice_KeepsItsWeakSigns_AndAStrayHaoLineIsListed()
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "갈 준비 됐소?", speaker: "0A0000D1"),
            Line(2, "난 그만한 값어치를 하오.", speaker: "0A0000D1"),
            Line(3, "나와 함께 가고 싶다면, 그들에게 떠나라고 말하시오.", speaker: "0A0000D1"),
            Line(4, "이미 동료가 있는 것 같군.", speaker: "0A0000D1"),
            Line(5, "그건 내가 책임질 거요.", speaker: "0A0000D1"),
            Line(6, "다음엔 어디요?", speaker: "0A0000D1"),
            Line(7, "때가 됐소. 갑시다.", speaker: "0A0000D1"),
            Line(8, "광산이 열린 뒤로 레이븐 락이 다시 살아나는 것 같소. 멋진 변화요.", speaker: "0A0000D1"),
            Line(9, "감히 말하건대, 제일 중요한 일일거요.", speaker: "0A0000D1"),
            Line(10, "그건 제가 해 드릴게요.", speaker: "0A0000D1"),
            Line(21, "그 말을 들으니 기쁘네. 잘됐어.", speaker: "0A0000D2"),
            Line(22, "마음만 먹으면 여길 전부 무너뜨릴 수도 있어.", speaker: "0A0000D2"),
            Line(23, "빨리 필요한 거 챙겨 와.", speaker: "0A0000D2"),
            Line(24, "다음엔 더 세게 덤벼 봐.", speaker: "0A0000D2"),
            Line(25, "그건 내 알 바 아니야.", speaker: "0A0000D2"),
            Line(26, "넌 정말 이상한 녀석이야.", speaker: "0A0000D2"),
            Line(27, "매그너스의 지팡이가 정말 해결책이라고 생각하는 거요?", speaker: "0A0000D2"),
            Line(31, "환영합니다.", speaker: "0A0000D3"),
            Line(32, "익숙하리라 믿습니다.", speaker: "0A0000D3"),
            Line(33, "기억해 두겠습니다.", speaker: "0A0000D3"),
            Line(34, "이제 물러가십시오.", speaker: "0A0000D3"),
            Line(35, "황량한 얼어붙은 땅의 구석구석까지, 그대가 마라 님의 온기를 전해주기를.", speaker: "0A0000D3"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var warned = issues.Where(i => i.Code == "speaker_tone_inconsistent").ToDictionary(i => i.Id, i => i.Message);
        Assert.Equal(new long[] { 10 }, warned.Keys);
        Assert.Contains("대부분 하오체, 이 대사 존댓말", warned[10]);
        var stray = Assert.Single(issues, i => i.Code == "speaker_tone_mixed");
        Assert.Equal(27, stray.Id);
        Assert.Contains("반말 6줄, 하오체 1줄, 이 줄 하오체", stray.Message);
    }

    /// <summary>
    /// A noun with 요 whose end is also a 해요체 ending: "문서요" (서요), "동반자요" (자요) and the 하오체 "낼꺼요" (꺼요)
    /// counted as 존댓말 in the Korean patch's 하오체 voices. 해요체 forms that end the same way still are: "그래서요?",
    /// "잘 자요.", "불 좀 꺼요."
    /// </summary>
    [Theory]
    [InlineData("여기 그 문서요.", false)]
    [InlineData("자넨 믿을 만한 동반자요.", false)]
    [InlineData("내가 값을 낼꺼요.", false)]
    [InlineData("그래서요?", true)]
    [InlineData("오늘은 푹 잘 자요.", true)]
    [InlineData("불 좀 꺼요.", true)]
    // Teldryn (Extended Hireling Dialogue): the 하오체 "N이오" of a vowel-final noun. Cicero's 해요체 stays one.
    [InlineData("하릭은 짐승 같은 사내요.", false)]
    [InlineData("그냥 이상한 여자요.", false)]
    [InlineData("저 카젠지는 늘 우울해 보이는군. 딱한 친구요.", false)]
    [InlineData("아니, 나도 마찬가지요.", false)]
    [InlineData("좋은 생각이오, 조용히 갑시다. 드로거는 귀가 밝으니까.", false)]
    [InlineData("선생이 리스너라구요!", true)]
    [InlineData("리스너는, 그저... 듣지요!", true)]
    [InlineData("내일 산책 할래요?", true)]
    [InlineData("그럼 뭘 하나요?", true)]
    public async Task HaoVoice_NounsShapedLikeHaeyo_AreWeak(string line, bool warned)
    {
        var entries = new List<LqaScanEntry>
        {
            Line(1, "갈 준비 됐소?", speaker: "0A0000E1"),
            Line(2, "난 그만한 값어치를 하오.", speaker: "0A0000E1"),
            Line(3, "그들에게 떠나라고 말하시오.", speaker: "0A0000E1"),
            Line(4, "때가 됐소.", speaker: "0A0000E1"),
            Line(5, "이건 내 일이오.", speaker: "0A0000E1"),
            Line(6, line, speaker: "0A0000E1"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(warned, issues.Any(i => i.Id == 6 && i.Code == "speaker_tone_inconsistent"));
    }

    /// <summary>
    /// The review of the fix above: a real short 해요체 reply said once, the 요 left after a quotation, a reply left
    /// after a stage direction and the second of two names with apostrophes are still the speaker's level; only a
    /// repeated cry and a quoted book line are not. Each line is judged among four lines of the other level.
    /// </summary>
    [Theory]
    [InlineData("뭐요? 그게 무슨 뜻이오?", true, true)]
    [InlineData("뭐요? 그게 무슨 뜻이오?", false, false)]
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
    // Meridia's Order: Laracin's 반말 lets, orders and questions had no level (-자, -라, -ㄹ까, 건가).
    [InlineData("이 위기를 해결하고 나서 얘기하자.", true, true)]
    [InlineData("뜻대로 부려라, 챔피언.", true, true)]
    [InlineData("지금 날 놀리는 건 아니길 바라.", true, true)]
    [InlineData("우리 물건을 훔친 그 멍청이? 몰라.", true, true)]
    [InlineData("우리 둘이 한번 결투라도 해 볼까.", true, true)]
    [InlineData("그럼 질문이 있는 건가?", true, true)]
    // A line that only trails off: 텐데, 테니, 안 가 (존댓말 would be 텐데요); 합니까 is 존댓말.
    [InlineData("분명 더 쓸모 있는 일을 할 수 있을 텐데, 챔피언.", true, true)]
    [InlineData("두려워 마. 우릴 얕보는 자는 누구든 후회하게 될 테니.", true, true)]
    [InlineData("이건 믿음이 안 가.", true, true)]
    [InlineData("이제 어디로 갑니까?", false, true)]
    [InlineData("오래 걸리진 않을 테니.", false, false)]
    // Nouns and names with those endings are not 반말.
    [InlineData("그가 우리 교단의 지도자.", true, false)]
    [InlineData("여기가 바로 그들의 나라.", true, false)]
    [InlineData("제 이름은 알라라.", true, false)]
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
