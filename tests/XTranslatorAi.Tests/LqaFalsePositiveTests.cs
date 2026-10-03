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

    private static LqaScanEntry Row(long id, string rec, string dest, string source = "Source text.")
        => new(id, (int)id, $"EDID{id:000}", rec, StringEntryStatus.Done, source, dest);

    private static GlossaryEntry BuiltIn(long id, string source, string target)
        => new(id, null, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10,
            "Built-in default glossary");
}
