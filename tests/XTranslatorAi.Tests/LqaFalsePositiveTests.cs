using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.Lqa.Internal;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Sentences from real projects that the quality check flagged although they were correct
/// (2026-10-02: 136 of 138 warnings on one plugin), next to the errors it must still find.
/// </summary>
public class LqaFalsePositiveTests
{
    private static readonly string[] Terms = { "날개를 잃은 우마릴", "세계의 목", "모로윈드", "화이트런", "드래곤" };

    [Theory]
    [InlineData("림 전기 발동 효과")]
    [InlineData("파워 차지 레벨 2 초과")]
    [InlineData("마법 저항 증가")]
    [InlineData("연금술 전문가")]
    [InlineData("효과가 있는 마법")]
    [InlineData("보호를 받는 대상")]
    [InlineData("더 나은 무기를 만든다.")]
    [InlineData("마을 사람들")]
    [InlineData("수은 광석")]
    [InlineData("전투 돌입 시 기 5중첩을 모을 수 있습니다.")]
    [InlineData("모은 기를 소모합니다.")]
    [InlineData("기꺼이 돕겠다.")]
    [InlineData("하이 엘프와 그레이 메인")]
    [InlineData("그런가?")]
    [InlineData("모로윈드와 화이트런의 세계의 목이라는 곳")]
    public void ParticleMismatch_IgnoresWordEndings(string dest)
        => Assert.Null(LqaHeuristics.FindHangulParticleMismatchSuggestion(dest, Terms));

    [Theory]
    [InlineData("매지카을 흡수합니다.", "매지카을 → 매지카를")]
    [InlineData("강철 검를 들었다.", "검를 → 검을")]
    [InlineData("모로윈드과 스카이림", "모로윈드과 → 모로윈드와")]
    [InlineData("화이트런와 솔리튜드", "화이트런와 → 화이트런과")]
    [InlineData("날개를 잃은 우마릴와 함께", "날개를 잃은 우마릴와 → 날개를 잃은 우마릴과")]
    [InlineData("세계의 목라고 불린다", "세계의 목라 → 세계의 목이라")]
    public void ParticleMismatch_FindsWrongParticles(string dest, string expected)
        => Assert.Equal(expected, LqaHeuristics.FindHangulParticleMismatchSuggestion(dest, Terms));

    // A Latin word is read as a word, so its last letter does not decide the particle (스톤을, 넥서스를, 룬을);
    // a Roman numeral is read as a number (칠세). Correct particles after acronyms are not flagged either.
    [Theory]
    [InlineData("Rune Stone을 찾았다.")]
    [InlineData("Rune을 새겼다.")]
    [InlineData("Nexus를 방문하세요.")]
    [InlineData("Enter를 누르세요.")]
    [InlineData("Aela을 만났다.")]
    [InlineData("Magicka을 흡수합니다.")]
    [InlineData("NPC는 공격하지 않는다.")]
    [InlineData("NPC가 말을 건다.")]
    [InlineData("HP가 30% 미만일 때")]
    [InlineData("DLC를 설치해야 한다.")]
    [InlineData("MCM을 엽니다.")]
    [InlineData("HTML은 지원하지 않습니다.")]
    [InlineData("Uriel Septim VII은 암살당했다.")]
    public void RomanParticleMismatch_IgnoresWordsReadAsWords(string dest)
        => Assert.Null(LqaHeuristics.FindRomanParticleMismatchSuggestion(dest));

    // An all-caps acronym is read letter by letter; only L, M, N and R (엘, 엠, 엔, 알) end in a consonant.
    [Theory]
    [InlineData("NPC을 고용합니다.", "NPC을 → NPC를")]
    [InlineData("NPC은 공격하지 않는다.", "NPC은 → NPC는")]
    [InlineData("HP이 30% 미만일 때", "HP이 → HP가")]
    [InlineData("MCM를 엽니다.", "MCM를 → MCM을")]
    [InlineData("SKSE과 함께 설치", "SKSE과 → SKSE와")]
    public void RomanParticleMismatch_FindsWrongParticleAfterAcronym(string dest, string expected)
        => Assert.Equal(expected, LqaHeuristics.FindRomanParticleMismatchSuggestion(dest));

    // The fixer leaves these verb forms alone for the same reason the quality check does.
    [Theory]
    [InlineData("뒤이은 혼란 속에서")]
    [InlineData("죄지은 자는 벌을 받는다.")]
    [InlineData("끌어모은 군대")]
    [InlineData("끌어모을 수 있다.")]
    [InlineData("두 사건을 관련지을 증거")]
    public void ParticleMismatch_IgnoresIrregularVerbForms(string dest)
        => Assert.Null(LqaHeuristics.FindHangulParticleMismatchSuggestion(dest, Terms));

    [Theory]
    [InlineData("지팡이가 정말 훌륭하다.")]
    [InlineData("성능 차이가 존재합니다.")]
    [InlineData("치명일격이 가능해집니다.")]
    [InlineData("그대가 이 책을 읽는다.")]
    [InlineData("야를을 믿지 못한다.")]
    [InlineData("연구 성과와 필생의 노력")]
    [InlineData("논리와 과학의 탐구")]
    public void DoubledParticle_IgnoresCorrectText(string dest)
        => Assert.Null(LqaHeuristics.FindDoubledParticleExample(dest, Terms));

    [Theory]
    [InlineData("매지카을를 흡수합니다.", "을를")]
    [InlineData("드래곤이가 나타났다.", "드래곤이가")]
    public void DoubledParticle_FindsDoubles(string dest, string expected)
        => Assert.Equal(expected, LqaHeuristics.FindDoubledParticleExample(dest, Terms));

    [Theory]
    [InlineData("Weapon Art - Power Bash execution MCO", "전기 - 강력한 밀어치기 실행 MCO")]
    [InlineData("npc addition weapon art", "NPC 추가 전기")]
    [InlineData("Weapon Art - Attack Execution (MCO1.6 Sprint Attack)", "전기 - 공격 실행 (MCO1.6 질주 공격)")]
    [InlineData("BloodSword01 Evoker-Knight - Left Sword  [ARMO:05000A6E]", "BloodSword01 소환사 기사 - 왼손 검  [ARMO:05000A6E]")]
    [InlineData("Detectable direction key WASD", "감지 가능한 방향키 WASD")]
    [InlineData("NPC Lv3 Weapon Art", "NPC 전기 Lv3")]
    [InlineData("Page one [page break] Page two", "1쪽 [page break] 2쪽")]
    [InlineData("Afterglow Qi - 30-ex", "잔광의 투지 - 30-ex")]
    [InlineData("EldenRimUpdate", "EldenRim 업데이트")]
    [InlineData("DLC1NPCMentalModelCureForeshadowTopic02VampInvisCont", "DLC1 NPC 정신 모델 치료 복선 주제 02 흡혈귀 투명 후속")]
    [InlineData("SDA_OPResponse2", "SDA_OP반응2")]
    [InlineData("SDA_NPCBanterMain", "SDA_NPC 만담 메인")]
    [InlineData("Launch - Rune Impact lv2", "발동 - 룬 임팩트 Lv2")]
    public void EnglishResidue_IgnoresAcronymsAndIdentifiersFromSource(string source, string dest)
        => Assert.Null(LqaScanner.FindEnglishResidue(dest, source));

    [Theory]
    [InlineData("you can forge the [Hand Strap]", "[Hand Strap]을 제작할 수 있습니다.", "Hand")]
    [InlineData("Meet Aela", "Aela을 만나기", "Aela")]
    [InlineData("what I call the 'Deep Venue'", "'심층 광장(Deep Venue)'이라 부르는 구조", "Deep")]
    [InlineData("an ex soldier", "ex 군인", "ex")]
    [InlineData("Afterglow Qi - 30-ex", "잔광의 투지 - ex", "ex")]
    [InlineData("EldenRimUpdate", "Elden 업데이트", "Elden")]
    public void EnglishResidue_FindsUntranslatedWords(string source, string dest, string expected)
        => Assert.Equal(expected, LqaScanner.FindEnglishResidue(dest, source));

    [Theory]
    [InlineData("저자:\nThe One", null)]
    [InlineData("저자:\n유일자", "The")]
    public void EnglishResidue_AcceptsWordsTheEarlierReleaseAlsoKept(string previous, string? expected)
        => Assert.Equal(expected, LqaScanner.FindEnglishResidue("저자:\nThe One", "by\nThe One", previous));

    [Fact]
    public async Task EnglishResidue_AcceptsTermsTheGlossaryKeepsInLatin()
    {
        // The built-in glossary maps "Thu'um" to "Thu'um", so the translation is meant to keep it.
        var entries = new List<LqaScanEntry> { Row(1, "INFO:NAM1", "너의 Thu'um과 내 마법이라면 거뜬해.", source: "Your Thu'um and my magic.") };
        var glossary = new List<GlossaryEntry> { BuiltIn(1, "Thu'um", "Thu'um") };

        var issues = await LqaScanner.ScanAsync(entries, "ko", glossary);

        Assert.DoesNotContain(issues, i => i.Code == "english_residue");
    }

    [Fact]
    public async Task EnglishResidue_ReadsThePreviousTranslationOfEachRow()
    {
        var entries = new List<LqaScanEntry>
        {
            new(1, 1, "EldenBook00", "BOOK:DESC", StringEntryStatus.Done, "by The One", "저자: The One", PreviousText: "저자: The One"),
            new(2, 2, "EldenBook01", "BOOK:DESC", StringEntryStatus.Done, "by The One", "저자: The One"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 2 }, issues.Where(i => i.Code == "english_residue").Select(i => i.Id));
    }

    [Theory]
    [InlineData("적의 일부 공격은 슈퍼아머 효과를 방해합니다. 효과 역시 해제됩니다.", "Hamnida")]
    [InlineData("더욱 특별한 반지를 획득하는 데 사용됩니다.", "Hamnida")]
    [InlineData("함께 갑시다!", "Hamnida")]
    [InlineData("대장간에 에보니 주괴를 넣어라.", "PlainDa")]
    [InlineData("내 징표를 착용하는 것을 고려해 보아라.", "PlainDa")]
    [InlineData("오늘따라 유난히 말이 많군, 헤르메우스 모라.", "Unknown")]
    public void ToneClassifier_ReadsPoliteAndImperativeEndings(string text, string expected)
        => Assert.Equal(expected, LqaToneClassifier.Classify(text).ToString());

    [Fact]
    public async Task RecTone_ComparesWithTheSameField_NotAFixedRegister()
    {
        var entries = new List<LqaScanEntry>
        {
            // Quest journals in 해라체 are the convention; none should be flagged.
            Row(1, "QUST:CNAM", "산적 두목을 처치해야 한다."),
            Row(2, "QUST:CNAM", "보상을 받았다."),
            Row(3, "QUST:CNAM", "책을 살펴봐야겠다."),
            Row(4, "QUST:CNAM", "그를 데리고 나와야 한다."),
            // Titles have no sentence ending: "편지" is not casual speech.
            Row(5, "BOOK:FULL", "삽비욘의 편지"),
            // Mostly 합니다체 messages with one 해라체 outlier.
            Row(6, "MESG:DESC", "효과가 해제됩니다."),
            Row(7, "MESG:DESC", "무기를 제작할 수 있습니다."),
            Row(8, "MESG:DESC", "반지와 룬을 획득하는 데 사용됩니다."),
            Row(9, "MESG:DESC", "슈퍼아머가 발동합니다."),
            Row(10, "MESG:DESC", "추가 피해를 준다."),
        };

        var issues = await LqaScanner.ScanAsync(entries, targetLang: "ko", forceTokenGlossary: new List<GlossaryEntry>());

        var tone = Assert.Single(issues, i => i.Code == "rec_tone");
        Assert.Equal(10, tone.Id);
        Assert.Contains("대부분 합니다체", tone.Message);
    }

    [Fact]
    public void GlossaryMissing_FollowsTheTranslatorsMatchingRules()
    {
        var glossary = new List<GlossaryEntry>
        {
            BuiltIn(1, "Elder Scroll", "엘더스크롤"),
            BuiltIn(2, "Destruction", "파괴마법"),
            BuiltIn(3, "Scroll", "주문서"),
        };

        // A lowercase ordinary word is not forced, so its absence is not a missing term.
        Assert.Null(LqaHeuristics.FindMissingForceTokenGlossaryTerm("the destruction of the city", "도시의 파괴", glossary));
        // Text a longer term already covered is not matched again by a shorter one.
        Assert.Null(LqaHeuristics.FindMissingForceTokenGlossaryTerm("He read the Elder Scroll.", "그는 엘더스크롤을 읽었다.", glossary));
        // The capitalized term is forced, so a translation without it is reported.
        var missing = LqaHeuristics.FindMissingForceTokenGlossaryTerm("Destruction spells", "파괴 주문", glossary);
        Assert.Equal("Destruction", missing?.SourceTerm);
    }

    [Theory]
    [InlineData("SDA_CellTrackMGEFTG", true)]
    [InlineData("SDA_DA05PostTopic02InvisCont", true)]
    [InlineData("EldenRimUpdate", true)]
    [InlineData("Dagger of Night", false)]
    [InlineData("Daggers", false)]
    public void Untranslated_IgnoresInternalIdentifiers(string text, bool identifier)
        => Assert.Equal(!identifier, LqaHeuristics.IsLikelyUntranslated(text, text));

    [Fact]
    public async Task DialogueTone_IgnoresNamesAndPlainStatementsInCasualSpeech()
    {
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "Hey.", "안녕, 뭐 해?"),
            Dialogue(2, "Let's go.", "가자, 시간이 없어."),
            Dialogue(3, "Fine.", "좋아, 그렇게 해."),
            Dialogue(4, "Really?", "정말 그런 거야?"),
            Dialogue(5, "Samantha.", "사만다."),
            Dialogue(6, "I don't know.", "나도 모르겠다."),
            Dialogue(7, "Is that so?", "그런가요?"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 7 }, issues.Where(i => i.Code == "tone_inconsistent").Select(i => i.Id));
    }

    /// <summary>
    /// An INFO's prompt and responses share its EDID: Serana's 존댓말 answer was marked against the player's 반말 prompt.
    /// The player's choices are compared with each other only, and an odd one among them is still marked.
    /// </summary>
    [Fact]
    public async Task DialogueTone_ComparesPlayerChoicesApartFromTheNpcsLines()
    {
        static LqaScanEntry Line(long id, string rec, string dest)
            => new(id, (int)id, "[05515D2E]", rec, StringEntryStatus.Done, "x", dest);
        var answer = new List<LqaScanEntry>
        {
            Line(1, "INFO:RNAM", "확실해? 이 흡혈귀들은 네 친구들 아니야?"),
            Line(2, "INFO:RNAM", "그럼 같이 가자."),
            Line(3, "INFO:RNAM", "정말 그렇게 할 거야?"),
            Line(4, "INFO:NAM1", "저조차 참는 데 한계가 있다고요!"),
        };
        Assert.DoesNotContain(await LqaScanner.ScanAsync(answer, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");

        var choices = new List<LqaScanEntry>
        {
            new(1, 1, "EGG1FlintFriendship1", "DIAL:FULL", StringEntryStatus.Done, "x", "고마워요, 즐겁게 들었어요."),
            new(2, 2, "EGG1FlintFriendship2", "DIAL:FULL", StringEntryStatus.Done, "x", "기분이 좀 나아졌나요?"),
            new(3, 3, "EGG1FlintFriendship3", "DIAL:FULL", StringEntryStatus.Done, "x", "저도 당신 덕분에 행복해요."),
            new(4, 4, "EGG1FlintFriendship4", "DIAL:FULL", StringEntryStatus.Done, "x", "그게 뭐지?"),
        };
        var issue = Assert.Single(await LqaScanner.ScanAsync(choices, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");
        Assert.Equal(4L, issue.Id);
        Assert.Contains("플레이어 선택지", issue.Message);
    }

    /// <summary>
    /// Plugin dialogue has no EDID, so the tone check never ran: Serana Dialogue Add-On had 359 lines in 해요체 among
    /// her 반말 lines and the review changed them by hand with no warning. Such lines are now compared with the
    /// plugin's dialogue as a whole, as information only, since another speaker may talk differently.
    /// </summary>
    [Fact]
    public async Task DialogueTone_WithoutEditorIds_IsComparedWithThePluginsDialogue()
    {
        static LqaScanEntry Line(long id, string dest, string rec = "INFO:NAM1")
            => new(id, (int)id, null, rec, StringEntryStatus.Done, "x", dest);
        var entries = new List<LqaScanEntry>
        {
            Line(1, "가자, 시간이 없어."),
            Line(2, "그건 내가 할게. 걱정하지 마."),
            Line(3, "정말 그런 거야?"),
            Line(4, "좋아, 그렇게 해."),
            Line(5, "넌 여기 있어야 해."),
            Line(6, "저도 같이 갈게요."),
            Line(7, "무엇을 도와드릴까요?", rec: "DIAL:FULL"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "tone_differs_from_plugin");
        Assert.Equal((6L, "Info"), (issue.Id, issue.Severity));
        Assert.DoesNotContain(issues, i => i.Code == "tone_inconsistent");
    }

    /// <summary>
    /// Once Serana Dialogue Add-On's lines had speakers, her 반말 still set the plugin's level: her lines to her father
    /// and a bard's lines to her were marked as unlike the plugin. A line with a known speaker is compared with that
    /// speaker's others only, and the lines without one with each other.
    /// </summary>
    [Fact]
    public async Task DialogueTone_LinesWithAKnownSpeaker_AreNotComparedWithThePluginsDialogue()
    {
        static LqaScanEntry Line(long id, string dest, string? speaker)
            => new(id, (int)id, null, "INFO:NAM1", StringEntryStatus.Done, "x", dest, Speaker: speaker);
        var entries = new List<LqaScanEntry>
        {
            Line(1, "정말 그런 거야?", "02002B6C"),
            Line(2, "좋아, 그렇게 해.", "02002B6C"),
            Line(3, "넌 여기 있어야 해.", "02002B6C"),
            Line(4, "정말 그런 거야?", "02002B6C"),
            Line(5, "좋아, 그렇게 해.", "02002B6C"),
            Line(6, "넌 여기 있어야 해.", "02002B6C"),
            Line(7, "정말 그런 거야?", "02002B6C"),
            Line(8, "좋아, 그렇게 해.", "02002B6C"),
            Line(9, "넌 여기 있어야 해.", "02002B6C"),
            Line(10, "정말 그런 거야?", "02002B6C"),
            Line(11, "아버지, 저 왔어요.", "02002B6C>02003BA8"),
            Line(12, "어서 오세요, 손님. 한 곡 들려드릴까요?", "0001A696"),
            Line(13, "물론이죠! 좋은 곡이네요!", null),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code == "tone_differs_from_plugin");
    }

    /// <summary>
    /// The player's prompt with no speaker condition was counted with the NPC lines of a plugin without EDIDs, and its
    /// 반말 was marked against their 해요체: the case the EDID groups had already fixed.
    /// </summary>
    [Fact]
    public async Task DialogueTone_PlayerPromptsWithoutASpeaker_StayOutOfThePluginsDialogue()
    {
        static LqaScanEntry Line(long id, string dest, string rec = "INFO:NAM1")
            => new(id, (int)id, null, rec, StringEntryStatus.Done, "x", dest);
        var entries = new List<LqaScanEntry>
        {
            Line(1, "어서 오세요."),
            Line(2, "필요한 게 있나요?"),
            Line(3, "조심하세요."),
            Line(4, "그게 뭐지?", rec: "INFO:RNAM"),
        };

        Assert.DoesNotContain(await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_differs_from_plugin");
    }

    /// <summary>
    /// 합니다체 and 해요체 mix in one NPC's speech ("환영합니다. 참으로 흥미롭군요."), as the speaker check reads them; the
    /// dialogue check counted them as two levels and marked the 해요체 line. 반말 among them is still marked.
    /// </summary>
    [Fact]
    public async Task DialogueTone_HamnidaAndHaeyo_AreOneLevel()
    {
        static LqaScanEntry Line(long id, string dest)
            => new(id, (int)id, "MerchantGreeting" + id, "INFO:NAM1", StringEntryStatus.Done, "x", dest);
        var polite = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."), Line(2, "기억해 두겠습니다."), Line(3, "물건을 보여 드리겠습니다."), Line(4, "참으로 흥미롭군요."),
        };
        Assert.DoesNotContain(await LqaScanner.ScanAsync(polite, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");

        // A persuasion or threat takes its own attitude, as the speaker check reads it.
        var choices = new List<LqaScanEntry>
        {
            new(1, 1, "XTopic1", "DIAL:FULL", StringEntryStatus.Done, "x", "도와줄 수 있나요?"),
            new(2, 2, "XTopic2", "DIAL:FULL", StringEntryStatus.Done, "x", "어디로 가면 되나요?"),
            new(3, 3, "XTopic3", "DIAL:FULL", StringEntryStatus.Done, "x", "고마워요."),
            new(4, 4, "XTopic4", "DIAL:FULL", StringEntryStatus.Done, "x", "다시 올게요."),
            new(5, 5, "XTopic5", "DIAL:FULL", StringEntryStatus.Done, "(Intimidate) Move. Now.", "(위협) 당장 비켜. 안 그러면 죽는다."),
        };
        Assert.DoesNotContain(await LqaScanner.ScanAsync(choices, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");

        var withCasual = polite.Append(Line(5, "그게 뭐지?")).Append(Line(6, "환영합니다, 손님.")).ToList();
        var issue = Assert.Single(await LqaScanner.ScanAsync(withCasual, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");
        Assert.Equal(5L, issue.Id);
        Assert.Contains("대부분 존댓말", issue.Message);
    }

    /// <summary>
    /// Run over the user's 56 projects, 1.16.28's dialogue check marked stage directions and old prayers as 반말 among
    /// 존댓말 choices: "[양초를 제작하는 방법을 설명한다]", "…있네요. (울프릭의 서류철을 보여준다)",
    /// "…맹세하셨나이다." A direction is not speech, and 하소서체 is 존댓말.
    /// </summary>
    [Fact]
    public async Task DialogueTone_ReadsNeitherStageDirectionsNorPrayersAsCasual()
    {
        static LqaScanEntry Choice(long id, string dest)
            => new(id, (int)id, "DBMQuestTopic" + id, "DIAL:FULL", StringEntryStatus.Done, "x", dest);
        var choices = new List<LqaScanEntry>
        {
            Choice(1, "도와드릴 일이 있을까요?"), Choice(2, "고마워요."), Choice(3, "다시 올게요."), Choice(4, "알겠습니다."),
            Choice(5, "[양초를 제작하는 방법을 설명한다]"),
            Choice(6, "마침 딱 맞는 게 있네요. (울프릭의 서류철을 보여준다)"),
            Choice(7, "\"마라께서 우리를 당신의 자녀로 굽어살피겠다 맹세하셨나이다.\""),
        };

        Assert.DoesNotContain(await LqaScanner.ScanAsync(choices, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");
    }

    /// <summary>
    /// Lines without a known speaker are many people's: read with 합니다체 and 해요체 as one, IDE Thalmor's
    /// officials' 반말 was 30 notes against the others' 존댓말. The plugin-wide comparison keeps the two apart.
    /// </summary>
    [Fact]
    public async Task DialogueTone_PluginWide_KeepsHamnidaAndHaeyoApart()
    {
        static LqaScanEntry Line(long id, string dest) => new(id, (int)id, null, "INFO:NAM1", StringEntryStatus.Done, "x", dest);
        var entries = new List<LqaScanEntry>
        {
            Line(1, "환영합니다."), Line(2, "기억해 두겠습니다."), Line(3, "물건을 보여 드리겠습니다."),
            Line(4, "참으로 흥미롭군요."), Line(5, "또 오세요."), Line(6, "잠깐... 이건 대체 누구지?"),
        };

        Assert.DoesNotContain(await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_differs_from_plugin");
    }

    /// <summary>
    /// The shared classifier missed Flint's 반말 among 존댓말 choices ("…다르거든.", "…것 같아! 하하!", "…모르겠네.")
    /// and read the 하오체 "…있는거요?" as 해요체. The dialogue check reads lines as the speaker check does.
    /// </summary>
    [Fact]
    public async Task DialogueTone_ReadsCasualAndHaoEndingsTheSharedClassifierMissed()
    {
        static LqaScanEntry Choice(long id, string dest)
            => new(id, (int)id, "EGG1FlintTalk" + id, "DIAL:FULL", StringEntryStatus.Done, "x", dest);
        var choices = new List<LqaScanEntry>
        {
            Choice(1, "그거 좋군요. 개인적으로 전 스위트롤을 좋아합니다."), Choice(2, "스위트롤요! 원뿔 모양의 빵 같은 거예요."),
            Choice(3, "아, 언제 한번 꼭 드셔보게 해야겠네요."), Choice(4, "좋네요. 마음을 열어준 것 같아 기쁩니다."),
            Choice(5, "절대 그러지 않을 겁니다."), Choice(6, "절대로요, 약속합니다."),
            Choice(7, "어디 가시는 거죠? 그냥 가버리시면 안 됩니다."), Choice(8, "아끼는 마음에서 그 모든 일을 한 거라고요!"),
            Choice(9, "용서할게요."), Choice(10, "바로 그 기세예요."), Choice(11, "고마워요."), Choice(12, "다시 올게요."),
            Choice(13, "분명 좋아할 거야, 장담해. 야생에서 맛보던 것들과는 차원이 다르거든."),
            Choice(14, "그 자리에서 바로 죽어도 여한이 없을 것 같아! 하하!"),
            Choice(15, "문제없어. 다음번엔 스위트롤도 좀 가져올지 모르겠네."),
            Choice(16, "무슨 말을 하고 있는거요?"),
        };

        var marked = (await LqaScanner.ScanAsync(choices, "ko", new List<GlossaryEntry>()))
            .Where(i => i.Code == "tone_inconsistent").Select(i => i.Id).ToList();

        Assert.Equal(new long[] { 13, 14, 15, 16 }, marked);

        // A 해요체 "~같은 거요" is not 하오체, and a 하오체 speaker's sentence in ~지 beside one in ~소 is 하오체.
        choices[12] = Choice(13, "자, 당신에게 물어볼 만한 게 생각났어요. 일상적인 대화거리 같은 거요.");
        Assert.DoesNotContain(await LqaScanner.ScanAsync(choices, "ko", new List<GlossaryEntry>()), i => i.Id == 13 && i.Code == "tone_inconsistent");
        var hao = new List<LqaScanEntry>
        {
            Choice(1, "당신은 누구요?"), Choice(2, "내가 무엇을 해야 할지 말해 보시오."), Choice(3, "평화롭게 살기를 원하오."),
            Choice(4, "이제 어떻게 되는 거요?"), Choice(5, "그는 더 이상 마르카스를 통치하지 않소. 이제는 전부 실버블러드 가문의 손아귀에 들어갔지."),
        };
        Assert.DoesNotContain(await LqaScanner.ScanAsync(hao, "ko", new List<GlossaryEntry>()), i => i.Code == "tone_inconsistent");
    }

    [Fact]
    public async Task NameConsistency_FindsOneNameSpelledTwoWays_ButNotTitlesOrParticles()
    {
        // Serana Dialogue Add-On wrote Merovech as 메로베흐, 메로벡 and 메로베크 in different lines.
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "Merovech, how do you plan to find him?", "메로베흐, 그를 어떻게 찾을 생각이야?"),
            Dialogue(2, "I trust Merovech with my life.", "난 메로베흐에게 목숨을 맡길 수 있어."),
            Dialogue(3, "Merovech said the road is safe.", "메로베흐가 길은 안전하다고 했어."),
            Dialogue(4, "Ask Merovech about the ship.", "메로벡에게 배에 대해 물어봐."),
            // A one-syllable name with two particles is the same spelling.
            Dialogue(5, "How on Nirn did you do that?", "넌에서 그걸 어떻게 했어?"),
            Dialogue(6, "Across Nirn, nobody knows.", "넌은 넓어서 아무도 몰라."),
            Dialogue(7, "Nirn is vast.", "넌을 다 돌아볼 순 없어."),
            // Ordinary words capitalized in titles are not names.
            Dialogue(8, "Horror Sign", "공포의 징표"),
            Dialogue(9, "Battle Sign", "전투의 징후"),
            Dialogue(10, "Storm Sign", "폭풍의 징후"),
            Dialogue(11, "Frost Sign", "서리의 징후"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "name_inconsistent");
        Assert.Equal(4, issue.Id);
        Assert.Contains("Merovech → '메로벡' (다른 3행은 '메로베흐')", issue.Message);
    }

    [Fact]
    public async Task NameConsistency_ReportsEachMinoritySpelling_AgainstTheMostFrequentOne()
    {
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "Merovech, how do you plan to find him?", "메로베흐, 그를 어떻게 찾을 생각이야?"),
            Dialogue(2, "I trust Merovech with my life.", "난 메로베흐에게 목숨을 맡길 수 있어."),
            Dialogue(3, "Merovech said the road is safe.", "메로베흐가 길은 안전하다고 했어."),
            Dialogue(4, "Ask Merovech about the ship.", "메로벡에게 배에 대해 물어봐."),
            Dialogue(5, "Merovech is waiting.", "메로베크는 기다리고 있어."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var found = issues.Where(i => i.Code == "name_inconsistent").OrderBy(i => i.Id).ToList();
        Assert.Equal(new long[] { 4, 5 }, found.Select(i => i.Id));
        Assert.Contains("Merovech → '메로벡' (다른 3행은 '메로베흐')", found[0].Message);
        Assert.Contains("Merovech → '메로베크' (다른 3행은 '메로베흐')", found[1].Message);
    }

    [Fact]
    public async Task NameConsistency_IgnoresParticlesInterjectionsAndVerbs()
    {
        // Flint9 wrote Valenwood with 에서 and 라고요, Ashe wrote "Ugh" as 으윽 and 으으, and a skill description
        // wrote "Summon" as 소환하고 and 소환하여; none of them is one name spelled two ways.
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "I grew up in Valenwood, you know.", "난 발렌우드에서 자랐어."),
            Dialogue(2, "You mean Valenwood?", "발렌우드라고요?"),
            Dialogue(3, "So you love Valenwood?", "발렌우드라고요?"),
            Dialogue(4, "Ugh. That smell.", "으윽. 저 냄새."),
            Dialogue(5, "Ugh, not again.", "으으, 또야."),
            Dialogue(6, "Ugh! Leave me be.", "으으! 날 내버려 둬."),
            Dialogue(7, "Then Summon the bow.", "그다음 활을 소환하고 쏜다."),
            Dialogue(8, "First Summon the bow.", "먼저 활을 소환하여 쏜다."),
            Dialogue(9, "We Summon the bow.", "우리는 활을 소환하여 쏜다."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code == "name_inconsistent");
    }

    [Fact]
    public async Task NameConsistency_IgnoresAWordCapitalizedOnlyAtTheStartOfSentences()
    {
        // EldenWarAshPack1 began its skill descriptions with "Summon": 소환해 in most, 소환한 뒤 in the bow ones.
        // The capital of a sentence's first word is no sign of a name; Merovech, also only at sentence starts but
        // once in the middle of one, still is.
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "Summon a daedric warhammer, strike your enemy.", "데이드라제 전투 망치를 소환해 적을 가격합니다."),
            Dialogue(2, "Summon a greatsword and slash.", "대검을 소환해 벱니다."),
            Dialogue(3, "Summon a spear. Thrust it forward.", "창을 소환해 앞으로 찌릅니다."),
            Dialogue(4, "A skill of old. Summon a blade and spin.", "옛 기술입니다. 칼날을 소환해 회전합니다."),
            Dialogue(5, "Summon the greatbow, then twist the bowstring.", "대궁을 소환한 뒤 활시위를 비틉니다."),
            Dialogue(6, "Summon the bow, then fire an arrow high up.", "활을 소환한 뒤 화살을 높이 쏩니다."),
            Dialogue(11, "Merovech, how do you plan to find him?", "메로베흐, 그를 어떻게 찾을 생각이야?"),
            Dialogue(12, "Merovech said the road is safe.", "메로베흐가 길은 안전하다고 했어."),
            Dialogue(13, "Merovech is waiting.", "메로베흐는 기다리고 있어."),
            Dialogue(14, "Ask Merovech about the ship.", "메로벡에게 배에 대해 물어봐."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "name_inconsistent");
        Assert.Equal(14, issue.Id);
    }

    [Fact]
    public async Task NameConsistency_CountsTitleCaseRowsForWordsUsedAsNames()
    {
        // Serana Dialogue Add-On greets each player name ("Hey Drelorea!") and spelled it 드렐로레아 there but
        // 드렐로리아 in a line; greetings are title-case rows, which alone do not make a word a name.
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "Drelorea, are you okay?", "드렐로리아, 괜찮아?"),
            Dialogue(2, "Hey Drelorea!", "안녕, 드렐로레아!"),
            Dialogue(3, "Thanks Drelorea!", "고마워, 드렐로레아!"),
            Dialogue(4, "Bye Drelorea!", "잘 가, 드렐로레아!"),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "name_inconsistent");
        Assert.Equal(1, issue.Id);
        Assert.Contains("Drelorea → '드렐로리아' (다른 3행은 '드렐로레아')", issue.Message);
    }

    [Fact]
    public async Task GlossaryVariant_FindsTheLoanwordForATranslatedTerm_ButNotNativeWordsOrNamesOfTheLine()
    {
        // Serana Dialogue Add-On said 뱀파이어로 변한 for "since I was turned"; the glossary says 흡혈귀.
        var glossary = new List<GlossaryEntry>
        {
            Term("Vampire", "흡혈귀"),
            Term("Illusion", "환영마법"),
            Term("Superior", "중급"),
            Term("Cloak", "망토"),
            Term("Saryoni", "사요니"),
        };
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "I feel like I can breathe again for the first time since I was turned.", "뱀파이어로 변한 이후 처음으로 다시 숨을 쉴 수 있게 된 것만 같아."),
            // The source names the vampire: a missing glossary term, reported as such.
            Dialogue(2, "You're a vampire?", "너 흡혈귀야?"),
            // Native words that sound like a term: 알아선 (Illusion), 수업이라도 (Superior), 그라아악 (Cloak).
            Dialogue(3, "The wizards know about all kinds of things.", "마법사들은 온갖 걸 다 알아선 안 될 것까지 알아."),
            Dialogue(4, "Maybe I should take a class.", "나도 수업이라도 들어볼까."),
            Dialogue(5, "Grrah!", "그라아악!"),
            // Verbs with 되다 or 버리다: 부여될 (Portal), 써버리는 (Superior).
            Dialogue(9, "It will be granted.", "힘이 부여될 거야."),
            Dialogue(10, "He spends it all.", "다 써버리는 사람이야."),
            // A name whose first vowel does not fit the term: 카리우스 (Carius) is not cuirass (퀴/큐).
            Dialogue(11, "This \"Falx Caius\" became a vampire.", "이 \"폭스 카리우스\"라는 자가 흡혈귀가 됐어."),
            // A name of the line (Sefirah), and a name the glossary spells out (Saryoni → 사요니, not 세라나).
            Dialogue(6, "Sefirah Missile", "세피라 미사일"),
            Dialogue(7, "Serana, look.", "세라나, 이것 봐."),
            // The plural names the same word: 마스터 for the Ideal Masters.
            Dialogue(8, "The Ideal Masters are real.", "이상적인 마스터는 실재해."),
        };
        glossary.Add(Term("Master", "달인"));
        glossary.Add(Term("Portal", "차원문"));
        glossary.Add(Term("Cuirass", "흉갑"));

        var issues = await LqaScanner.ScanAsync(entries, "ko", glossary);

        var issue = Assert.Single(issues, i => i.Code == "glossary_variant");
        Assert.Equal(1, issue.Id);
        Assert.Equal("용어집과 다른 표기: '뱀파이어' → '흡혈귀' (Vampire)", issue.Message);
    }

    [Fact]
    public async Task GlossaryVariant_TermWordInSourceUsedAsCommonNoun_IsStillReported()
    {
        // Serana: "vampires are powerful" → 뱀파이어는 강력하지만 (43 rows, all corrected to 흡혈귀). The loanword spells
        // a word of the source, but that word is the glossary term itself, used as a common noun.
        var glossary = new List<GlossaryEntry> { Term("Vampire", "흡혈귀"), Term("Master", "달인") };
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "Even if vampires are powerful, they can die.", "뱀파이어는 강력하지만 죽을 수 있어."),
            Dialogue(2, "Vampires are powerful.", "뱀파이어는 강력해."),
            Dialogue(3, "Are you a pure-blooded vampire?", "너 순혈 뱀파이어야?"),
            // A capitalized name inside the line stays as written: the Ideal Masters.
            Dialogue(4, "The Ideal Masters are real.", "이상적인 마스터는 실재해."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", glossary);

        Assert.Equal(new long[] { 1, 2, 3 }, issues.Where(i => i.Code == "glossary_variant").Select(i => i.Id).OrderBy(i => i).ToArray());
    }

    // Feris: 브레이스 is Braith (th written as ㅅ), not Flesh; 버텨봐 ("hold on") is a verb, not Fortify; and 마스터 키
    // spells "master key" whole, so 마스터 is not the glossary's Master (달인).
    [Fact]
    public async Task GlossaryVariant_IgnoresNamesWithTh_VerbsWithBoda_AndWholeLoanwordPhrases()
    {
        var glossary = new List<GlossaryEntry> { Term("Flesh", "피부"), Term("Fortify", "강화"), Term("Master", "달인"), Term("Illusion", "환영마법") };
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "I overheard Braith trying to talk to her parents.", "브레이스가 부모님이랑 얘기하려는 걸 우연히 들었어."),
            Dialogue(2, "In the meantime, keep holding on.", "그동안은 계속 버텨봐."),
            Dialogue(3, "Should be a master key somewhere around here.", "이 근처 어딘가에 마스터 키가 있을 거야."),
            // A plural next word: "files" is written 파일.
            Dialogue(4, "Load order: sort the master files first.", "로드 순서: 마스터 파일을 먼저 정렬하세요."),
            // 선 is 에선 shortened: 아래선 is not Illusion (Teldryn in the Codex trial).
            Dialogue(5, "You may want to be careful down here.", "여기 아래선 조심하는 게 좋겠소."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", glossary);

        Assert.DoesNotContain(issues, i => i.Code == "glossary_variant");
    }

    // Feris: a name with an ending after it is the same spelling (노드일 뿐, 아케이였어, 노드들), and Shor's Stone
    // (쇼어스 스톤) is not Shor spelled 쇼어스. A different spelling (노르드) is still reported.
    [Fact]
    public async Task NameInconsistent_IgnoresEndingsAfterTheName()
    {
        var entries = new List<LqaScanEntry>
        {
            Dialogue(1, "The tug of war between the Forsworn and the Nords.", "포스원과 노르드 사이의 줄다리기 말이야."),
            Dialogue(2, "True Nords of Skyrim they call themselves. They're just Nords playing in the hills to me.", "스스로 진정한 스카이림의 노드라고 부르더군. 내가 보기엔 그냥 언덕에서 놀고 있는 노드일 뿐이지만."),
            Dialogue(3, "What else could the ancient Nords do?", "고대 노드들이 달리 뭘 할 수 있었겠어?"),
            Dialogue(4, "As there are for the Nords.", "노드들에게 많은 것처럼 말이야."),
            Dialogue(5, "Those that don't look like Nords.", "노드처럼 생기지 않은 사람들에게도 그렇고."),
            Dialogue(6, "He doesn't understand much about Nords at all.", "노드에 대해 아는 게 거의 없는 것 같아."),
            Dialogue(11, "How can a Priest of Arkay be afraid of the dead?", "아케이의 사제가 어떻게 시체를 두려워할 수가 있지?"),
            Dialogue(12, "Acolytes of Arkay must come through here.", "아케이의 복사들이 여길 지나가겠지."),
            Dialogue(13, "Priests of Arkay are better than I.", "아케이의 사제들은 나보다 훨씬 대단해."),
            Dialogue(14, "Unless the priest of Arkay hides a secret.", "아케이의 사제에게 숨기는 비밀이 있는 게 아니라면."),
            Dialogue(15, "His last thoughts were for his love, and Arkay.", "마지막 순간에 떠올린 건 연인과 아케이였어."),
            Dialogue(21, "Those soldiers at Shor's Stone were afraid of spiders.", "쇼어스 스톤의 병사들, 거미를 무서워했던 거 맞지?"),
            Dialogue(22, "Sylgja, from Shor's Stone.", "쇼어스 스톤의 실기야 말이야."),
            Dialogue(23, "How did this place become Shor's Stone?", "어쩌다 여기가 쇼어스 스톤이 된 거지?"),
            Dialogue(24, "If Shor's Stone can't mine, the town's sunk.", "쇼어스 스톤에서 채광을 못 하면 마을이 망할 거야."),
            Dialogue(25, "Shor and Tsun. They're the gods who fought.", "쇼어와 쑨. 싸우다 죽은 신들이지."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 1 }, issues.Where(i => i.Code == "name_inconsistent").Select(i => i.Id).ToArray());
    }

    private static GlossaryEntry Term(string source, string target)
        => new(0, null, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null);

    private static LqaScanEntry Dialogue(long id, string source, string dest)
        => new(id, (int)id, "SDA_TalkTopic", "INFO:NAM1", StringEntryStatus.Done, source, dest);

    [Fact]
    public async Task TmFallback_IsInformationOnly()
    {
        var entries = new List<LqaScanEntry> { Row(1, "MGEF:FULL", "화염 피해", source: "Fire Damage") };
        var notes = new Dictionary<long, string> { [1] = "TM 폴백: 같은 원문의 REC/EDID/대화 문맥이 달라 개별 번역합니다." };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>(), tmFallbackNotes: notes);

        Assert.Equal("Info", Assert.Single(issues).Severity);
    }

    // MEI listed 136 "TM 대신 번역" notes and none needed action: 131 were dialogue, which is translated on its own on
    // purpose (the official lines use another speaker's tone), and 5 were names that came out as the memory's own
    // translation. A fallback elsewhere (a TM entry that broke a placeholder) is still listed.
    [Fact]
    public async Task TmFallback_IsNotListedForDialogue_OrForANameMatchingTheMemory()
    {
        var entries = new List<LqaScanEntry>
        {
            new(1, 1, "Topic", "DIAL:FULL", StringEntryStatus.Done, "Who are the Thalmor?", "탈모어는 누구입니까?"),
            new(2, 2, null, "INFO:NAM1", StringEntryStatus.Done, "I'm listening.", "듣고 있어."),
            new(3, 3, "MS03Rulindil", "NPC_:FULL", StringEntryStatus.Done, "Rulindil", "룰린딜"),
            new(4, 4, "FireDmg", "MGEF:FULL", StringEntryStatus.Done, "Fire Damage", "화염 피해"),
        };
        var notes = new Dictionary<long, string>
        {
            [1] = "TM 폴백: 직접 플러그인의 INFO/DIAL 대화는 개별 번역합니다.", [2] = "TM 폴백: 직접 플러그인의 INFO/DIAL 대화는 개별 번역합니다.",
            [3] = "TM 폴백: 같은 원문의 REC/EDID/대화 문맥이 달라 개별 번역합니다.", [4] = "TM 폴백: 같은 원문의 REC/EDID/대화 문맥이 달라 개별 번역합니다.",
        };
        var names = XTranslatorAi.Core.Translation.ReferenceNameIndex.Build(new[] { ("Rulindil", "룰린딜"), ("Speak to Rulindil", "룰린딜과 대화하기") });

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>(), tmFallbackNotes: notes, referenceNames: names);

        Assert.Equal(4L, Assert.Single(issues, i => i.Code == "tm_fallback").Id);
    }

    // MEI's last note was Rulindil → 룰린딜, a single name the name index does not hold: the translation is the
    // memory's own, so the note is dropped before the scan.
    [Fact]
    public void TmFallbackNotes_DropRowsTranslatedAsTheMemory()
    {
        var entries = new List<LqaScanEntry>
        {
            new(3, 3, "Rulindil", "NPC_:FULL", StringEntryStatus.Done, "Rulindil", "룰린딜"),
            new(4, 4, "FireDmg", "MGEF:FULL", StringEntryStatus.Done, "Fire Damage", "불 피해"),
        };
        var notes = new Dictionary<long, string> { [3] = "TM 폴백", [4] = "TM 폴백" };

        var kept = LqaScanner.DropTmFallbacksMatchingMemory(notes, entries, new[] { ("Rulindil", "룰린딜"), ("Fire Damage", "화염 피해") });

        Assert.Equal(new long[] { 4 }, kept.Keys);
    }

    // MEI Red Wave's sailor Eris is a mod NPC the user keeps as 에리스 through a forced glossary term, while the memory's
    // Eris is 이리스. The row says what the glossary says, so its fallback note needs no action either.
    [Fact]
    public async Task TmFallback_IsNotListedForRowsTranslatedAsTheGlossaryForces()
    {
        var entries = new List<LqaScanEntry>
        {
            new(3, 3, "SailorEris", "NPC_:FULL", StringEntryStatus.Done, "Eris", "에리스"),
            new(4, 4, "FireDmg", "MGEF:FULL", StringEntryStatus.Done, "Fire Damage", "불 피해"),
        };
        var notes = new Dictionary<long, string> { [3] = "TM 폴백", [4] = "TM 폴백" };
        var glossary = new List<GlossaryEntry>
        {
            new(1, null, "Eris", "에리스", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 20, null),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", glossary, tmFallbackNotes: notes);

        Assert.Equal(4L, Assert.Single(issues, i => i.Code == "tm_fallback").Id);
    }

    [Fact]
    public async Task BookLengthRatio_SkipsShortTitles_ButChecksLongBodies()
    {
        var entries = new List<LqaScanEntry>
        {
            Row(1, "BOOK:FULL", "마법책: 벼락", source: "Spell Tome: Thunderbolt"),
            Row(2, "BOOK:DESC", "짧은 요약.", source: string.Join(" ", Enumerable.Repeat("A long paragraph of lore.", 12))),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 2 }, issues.Where(i => i.Code == "book_length_ratio").Select(i => i.Id));
    }

    // Numbered steps and emoticons are unbalanced in the source too; only a bracket the translation
    // lost or added on its own is reported.
    [Theory]
    [InlineData("1) Gather herbs. 2) Grind them.", "1) 약초를 모은다. 2) 빻는다.", false)]
    [InlineData("See you soon :)", "곧 보자 :)", false)]
    [InlineData("Step [1 of 3", "단계 [1/3", false)]
    [InlineData("1) Gather herbs. 2) Grind them.", "1. 약초를 모은다. 2. 빻는다.", false)]
    [InlineData("Restore (50 points) of Health.", "체력을 (50포인트 회복한다.", true)]
    [InlineData("1) Gather herbs. 2) Grind them.", "1) 약초를 모은다. 빻는다.", true)]
    [InlineData("Restore 50 points.", "체력을 50포인트] 회복한다.", true)]
    public async Task BracketMismatch_ComparesWithTheSource(string source, string dest, bool expected)
    {
        var entries = new List<LqaScanEntry> { Row(1, "BOOK:DESC", dest, source) };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(expected, issues.Any(i => i.Code == "bracket_mismatch"));
    }

    // An agent's script cut answers after a separator; the run's checks passed them.
    [Theory]
    [InlineData("Atronach Forge Recipe - Staff of the Cold Flames", "아트로나크 대장간 제조법 -", true)]
    [InlineData("Spell Tome: Cold Flames", "마법책:", true)]
    [InlineData("Ingredients:", "재료:", false)]
    [InlineData("I was going to—", "나는 막—", false)]
    [InlineData("Staff of the Cold Flames", "지팡이 - 차가운 불길", false)]
    [InlineData("Cold Flames", "차가운 불길 ", false)]
    [InlineData("THE END", "- 끝 -", false)]
    [InlineData("But I...", "하지만 난—", false)]
    [InlineData("But I...", "하지만 난:", true)]
    public async Task CutOff_IsATranslationEndingAtASeparatorItsEnglishDoesNot(string source, string dest, bool expected)
    {
        var issues = await LqaScanner.ScanAsync(new List<LqaScanEntry> { Row(1, "BOOK:FULL", dest, source) }, "ko", new List<GlossaryEntry>());

        Assert.Equal(expected, issues.Any(i => i.Code == "cut_off"));
    }

    private static LqaScanEntry Row(long id, string rec, string dest, string source = "Source text.")
        => new(id, (int)id, $"EDID{id:000}", rec, StringEntryStatus.Done, source, dest);

    // A name the game fills in is unknown too; the official translation writes "<Alias=Victim>을/를" on purpose.
    [Theory]
    [InlineData("<Alias=Victim>을/를 죽인 것에 대한 감사표시?")]
    [InlineData("동의하오. 제국은 <Alias=ImperialMinorHold2>을/를 넘겨주어야 합니다.")]
    [InlineData("<Alias.ShortName=Target> 이/가 당신들에게 더 지불해 줄 것이다.")]
    [InlineData("<BaseName>을(를) 얻었습니다.")]
    public void ParticleMarker_AfterARuntimeName_IsNotReported(string text)
    {
        Assert.False(LqaHeuristics.HasUnresolvedParticleMarkers(text));
        Assert.Equal(text, KoreanTranslationFixer.Fix("korean", text));
    }

    // A runtime number's last digit is unknown, so writing the particle both ways after it is right.
    [Fact]
    public async System.Threading.Tasks.Task ParticleMarker_AfterARuntimeNumber_IsNotReported()
    {
        Assert.False(LqaHeuristics.HasUnresolvedParticleMarkers("기가 %.0f/%.0f(으)로 상승했습니다"));
        Assert.False(LqaHeuristics.HasUnresolvedParticleMarkers("<mag>을(를) 회복합니다"));
        Assert.True(LqaHeuristics.HasUnresolvedParticleMarkers("검을(를) 휘두릅니다"));

        var issues = await LqaScanner.ScanAsync(new[]
        {
            new LqaScanEntry(1, 1, "WarAshPointUP", "MESG:DESC", XTranslatorAi.Core.Models.StringEntryStatus.Done,
                "Qi is raised to %.0f/%.0f", "기가 %.0f/%.0f(으)로 상승했습니다", null),
        }, "korean", Array.Empty<GlossaryEntry>());
        Assert.DoesNotContain(issues, i => i.Code == "particle_marker");
    }

    private static GlossaryEntry BuiltIn(long id, string source, string target)
        => new(id, null, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10,
            "Built-in default glossary");
}
