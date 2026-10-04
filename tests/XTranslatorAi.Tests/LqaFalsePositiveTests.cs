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

    private static LqaScanEntry Row(long id, string rec, string dest, string source = "Source text.")
        => new(id, (int)id, $"EDID{id:000}", rec, StringEntryStatus.Done, source, dest);

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
