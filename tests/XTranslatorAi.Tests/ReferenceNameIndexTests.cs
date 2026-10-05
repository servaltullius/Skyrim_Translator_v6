using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Serana Dialogue Add-On mentioned vanilla names inside sentences, where the translation memory does not
/// apply, and the model guessed them (에란두르 for Erandur, 키케로 for Cicero, 사역마 소환 for Conjure Familiar).
/// </summary>
public class ReferenceNameIndexTests
{
    private static readonly (string, string)[] Memory =
    {
        ("Erandur", "에란더"),
        ("Speak to Erandur", "에란더와 대화하기"),
        ("Cicero", "시세로"),
        ("Talk to Cicero about the contract", "시세로와 계약에 대해 대화하기"),
        ("Conjure Familiar", "늑대 소환"),
        ("Auriel's Bow", "아우리엘의 활"),
        ("Bleak Falls Barrow", "황량한 폭포 무덤"),
        ("Maven Black-Briar", "메이븐 블랙-브라이어"),
        // Drinks and gems keep their whole name after the category.
        ("Honningbrew Mead", "술 - 허닝브루 벌꿀술"),
        // Ordinary words: used in lowercase elsewhere, or only ever as a whole entry.
        ("Ghost", "유령"),
        ("A ghost appears", "유령이 나타난다"),
        ("Slot", "장치"),
        // Not names: an objective, and inventory names whose category is part of the name.
        ("Find Esbern", "에스번을 찾기"),
        ("Abandoned Prison Key", "열쇠 - 버려진 감옥"),
        ("Ring of Archery", "반지 - 하급 궁술"),
    };

    private static readonly ReferenceNameIndex Index = ReferenceNameIndex.Build(Memory);

    [Theory]
    [InlineData("Erandur's goddess Mara.", "Erandur")]
    [InlineData("I followed Cicero to Dawnstar.", "Cicero")]
    [InlineData("Cast Conjure Familiar for me.", "Conjure Familiar")]
    [InlineData("I'd like to see Auriel's Bow in person.", "Auriel's Bow")]
    [InlineData("Back at Bleak Falls Barrow, ugh.", "Bleak Falls Barrow")]
    [InlineData("So Maven Black-Briar invited you?", "Maven Black-Briar")]
    [InlineData("A bottle of Honningbrew Mead, please.", "Honningbrew Mead")]
    public void FindsNamesInsideSentences(string text, string expected)
        => Assert.Equal(expected, Assert.Single(Index.FindIn(text)).Source);

    [Theory]
    [InlineData("Ghost! A ghost appeared.")]
    [InlineData("Put it in the Slot.")]
    [InlineData("Find Esbern for me.")]
    [InlineData("Where is the Abandoned Prison Key?")]
    [InlineData("I found a Ring of Archery.")]
    [InlineData("erandur, in lowercase, is not the name.")]
    [InlineData("Erandurian ruins.")]
    public void LeavesOrdinaryWordsAndNonNamesAlone(string text) => Assert.Empty(Index.FindIn(text));

    [Fact]
    public void BuildsFromNameEntriesOnly() => Assert.Equal(7, Index.Count);

    [Fact]
    public void DropsTheDrinkCategoryFromTheOfficialName()
        => Assert.Equal("허닝브루 벌꿀술", Assert.Single(Index.FindIn("Honningbrew Mead.")).Target);

    [Fact]
    public void ForcesNamesAsTermTokens_LongestFirst()
    {
        var applied = Index.ForceNames(new GlossaryApplication("Erandur's goddess and Auriel's Bow, not erandur.",
            new Dictionary<string, string>(), Array.Empty<(string, string)>()));

        Assert.Equal("__XT_TERM_N2_0000__'s goddess and __XT_TERM_N1_0000__, not erandur.", applied.Text);
        Assert.Equal("아우리엘의 활", applied.TokenToReplacement["__XT_TERM_N1_0000__"]);
        Assert.Equal("에란더", applied.TokenToReplacement["__XT_TERM_N2_0000__"]);
    }

    private static readonly ReferenceNameIndex MaterialIndex = ReferenceNameIndex.Build(new[]
    {
        ("Ebony Sword", "에보니 검"), ("Ebony Bow", "에보니 활"),
        ("Dragonscale Armor", "드래곤 비늘 방어구"), ("Dragonscale Boots", "드래곤 비늘 전투화"),
        ("Daedric Armor", "데이드라제 방어구"), ("Daedric Dagger", "데이드라제 단검"),
        ("Glass Armor", "글래스 방어구"), ("Glass Bow", "글래스 활"),
        ("Elven Armor", "엘프제 방어구"), ("Elven Bow", "엘프제 활"),
    });

    private static GlossaryApplication ForceMaterials(string text)
        => MaterialIndex.ForceNames(new GlossaryApplication(text, new Dictionary<string, string>(), Array.Empty<(string, string)>()));

    // Elden Rim: "Pure Ebony" came back as 흑단 and 흑연마석, "Dragonscale War Dance" as 용비늘 무답.
    [Theory]
    [InlineData("Pure Ebony - Whirlwind", "Pure __XT_TERM_N1_0000__ - Whirlwind", "에보니")]
    [InlineData("Ebony Beam - 120°trail", "__XT_TERM_N1_0000__ Beam - 120°trail", "에보니")]
    [InlineData("Dragonscale War Dance", "__XT_TERM_N1_0000__ War Dance", "드래곤 비늘")]
    [InlineData("Daedric armor is forged at night.", "__XT_TERM_N1_0000__ armor is forged at night.", "데이드라제")]
    [InlineData("Forge a Glass Katana.", "Forge a __XT_TERM_N1_0000__ Katana.", "글래스")]
    public void ForcesMaterialsOfItemNamesTheMemoryDoesNotHold(string text, string expected, string target)
    {
        var applied = ForceMaterials(text);

        Assert.Equal(expected, applied.Text);
        Assert.Equal(target, applied.TokenToReplacement["__XT_TERM_N1_0000__"]);
    }

    [Theory]
    [InlineData("A Daedric Lord walks among the Daedric ruins.")]
    [InlineData("The Glass Cannon perk.")]
    [InlineData("It was made of Ebony. Glass shatters.")]
    [InlineData("Ebony is rare, and ebony swords rarer.")]
    [InlineData("I've discovered a rare Snow Elven staff.")]
    public void LeavesMaterialWordsInOtherSensesAlone(string text) => Assert.Equal(text, ForceMaterials(text).Text);

    [Fact]
    public void ForcesMaterialsOnlyWhenTheMemorySpellsThem()
        => Assert.Equal("Pure Ebony - Whirlwind", Index.ForceNames(new GlossaryApplication("Pure Ebony - Whirlwind",
            new Dictionary<string, string>(), Array.Empty<(string, string)>())).Text);

    // MEI (Maven Elenwen Ingun): the glossary forces "Dibella" and "Black-Briar", which broke the longer official
    // names around them before the memory could see them: 디벨라의 요원/대리인 (Agent of Dibella → 디벨라의 사도),
    // 블랙-브라이어 산장 (Black-Briar Lodge → 블랙-브라이어 가옥), 블랙-브라이어 벌미주 (Black-Briar Mead → 벌꿀술).
    private static readonly GlossaryApplier DibellaGlossary = new(new[]
    {
        new GlossaryEntry(1, null, "Dibella", "디벨라", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
        new GlossaryEntry(2, null, "Black-Briar", "블랙-브라이어", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
        new GlossaryEntry(3, null, "Ingun Black-Briar", "잉건 블랙-브라이어", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
    });

    private static readonly ReferenceNameIndex DibellaNames = ReferenceNameIndex.Build(new[]
    {
        ("Agent of Dibella", "디벨라의 사도"), ("Black-Briar Lodge", "블랙-브라이어 가옥"), ("Black-Briar Mead", "술 - 블랙-브라이어 벌꿀술"),
        ("Ingun Black-Briar", "잉건 블랙-브라이어 아씨"),
    });

    private static string Resolve(GlossaryApplication applied)
        => applied.TokenToReplacement.Aggregate(applied.Text, (text, token) => text.Replace(token.Key, token.Value, StringComparison.Ordinal));

    [Theory]
    [InlineData("You wanna... what? Agent of Dibella? Dibella's faithful?", "You wanna... what? 디벨라의 사도? 디벨라's faithful?")]
    [InlineData("Really? In the lodge? As in \"Black-Briar Lodge\"?", "Really? In the lodge? As in \"블랙-브라이어 가옥\"?")]
    [InlineData("I plan to bring Black-Briar Mead to all of Tamriel.", "I plan to bring 블랙-브라이어 벌꿀술 to all of Tamriel.")]
    // A name the glossary forces whole keeps the glossary's translation.
    [InlineData("Ingun Black-Briar sends her regards.", "잉건 블랙-브라이어 sends her regards.")]
    public void OfficialNameContainingAGlossaryTerm_WinsOverTheShorterTerm(string source, string expected)
        => Assert.Equal(expected, Resolve(DibellaNames.ApplyWithGlossary(source, DibellaGlossary)));

    // MEI wrote Ingun as 인군 36 times: the memory names her only as "Ingun Black-Briar" (잉건 블랙-브라이어) and
    // "Ingun's Alchemy Chest", never alone, so the index had no "Ingun". A word of a full name becomes a name of its
    // own when the official translation spells it by sound; word order does not matter (발그루프 영주). The memory
    // must spell it that way in most entries that use it, and in at least two.
    private static readonly ReferenceNameIndex FullNames = ReferenceNameIndex.Build(new[]
    {
        ("Ingun Black-Briar", "잉건 블랙-브라이어"), ("Bring the deathbell to Ingun Black-Briar", "데스벨을 잉건에게 가져다 주기"),
        // 리트러쉬 is one sound off Letrush, accepted because the memory also writes "to Letrush" in a sentence.
        ("Louis Letrush", "루이 리트러쉬"), ("Talk to Letrush about the horse", "리트러쉬와 말에 대해 대화하기"),
        ("Jarl Balgruuf", "발그루프 영주"), ("Speak to Jarl Balgruuf", "발그루프 영주와 대화하기"),
        ("Stone of Barenziah", "바렌자이아의 보석"), ("Old Orc", "늙은 오크"), ("The old orc is here.", "늙은 오크가 여기 있다."),
        ("Talk to the jarl.", "영주와 대화하기"),
        // Sound alone would match Charming with the start of 지팡이; the spelling must be a whole word.
        ("Charming Staff", "매혹의 지팡이"), ("Use the Charming Staff", "매혹의 지팡이 사용"),
    });

    [Fact]
    public void WordsOfOfficialFullNames_AreNamesOfTheirOwn_WhenSpelledBySound()
    {
        var found = FullNames.FindIn("Ingun told Letrush that Balgruuf kept the Stone of Barenziah.", max: 16).ToDictionary(n => n.Source, n => n.Target);

        Assert.Equal("잉건", found["Ingun"]);
        Assert.Equal("리트러쉬", found["Letrush"]);
        Assert.Equal("발그루프", found["Balgruuf"]);
        Assert.Equal("바렌자이아의 보석", found["Stone of Barenziah"]);
        Assert.Empty(FullNames.FindIn("Jarl, the Old one, and Stone. Charming.", max: 16));
    }

    // The memory's own entry "Imperial Legion" says 임페리얼, but 10 of its 12 sentences say 제국군, so forcing the
    // entry wrote 임페리얼 into MEI's USSEP patch. A name its sentences rarely spell that way is not forced.
    [Fact]
    public void NameThatTheMemorysSentencesSpellDifferently_IsNotForced()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Imperial Legion", "임페리얼"), ("Hail the Imperial Legion!", "제국군 만세!"),
            ("How do I join the Imperial Legion?", "제국군에 가입하려면 어떻게 해야 됩니까?"),
            ("I want to join the Imperial Legion now.", "지금 제국군에 입대하고 싶습니다."),
            ("Maven Black-Briar", "메이븐 블랙-브라이어"), ("Speak to Maven Black-Briar today", "메이븐 블랙-브라이어와 오늘 대화하기"),
        });

        Assert.Empty(index.FindIn("Released by order of the Imperial Legion."));
        Assert.Equal("메이븐 블랙-브라이어", Assert.Single(index.FindIn("Ask Maven Black-Briar.")).Target);
    }

    // MEI's patches wrote Cairine as 케이린 and 케어린 (game: 카이린) and From-Deepest-Fathoms as 가장-깊은-곳에서-온-자:
    // a one-word entry counted only when a memory sentence also used it, which keeps item words like Slot (장치) out.
    // A one-word entry the memory spells by sound is a name on its own; Erdi (어디) has too few sounds to tell.
    [Fact]
    public void OneWordEntriesSpelledBySound_AreNames()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Cairine", "카이린"), ("From-Deepest-Fathoms", "프롬-디피스트-페덤스"), ("Slot", "장치"), ("Hawk", "매"), ("Erdi", "어디"),
        });

        var found = index.FindIn("Give it to Cairine and From-Deepest-Fathoms.").ToDictionary(n => n.Source, n => n.Target);
        Assert.Equal("카이린", found["Cairine"]);
        Assert.Equal("프롬-디피스트-페덤스", found["From-Deepest-Fathoms"]);
        Assert.Empty(index.FindIn("Slot, Hawk and Erdi."));
    }

    // MEI's Beast Races addon wrote "sweeter than moon sugar" as 달빛 사탕 and 달빛 설탕: the memory names it only as
    // "Moon Sugar" (문 슈거), and only capitalized names were matched. In lowercase, a multi-word name counts only when
    // its first word is spelled by sound (moon → 문): across local projects the lowercase forms of translated names were
    // ordinary phrases ("served on a silver platter", "bad enough to turn undead"). One-word names never count.
    [Fact]
    public void MultiWordNamesSpelledBySound_AreFoundInLowercaseToo()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Moon Sugar", "문 슈거"), ("Deliver Moon Sugar to the caravan", "카라반에 문 슈거를 배달하기"),
            ("Dirge", "더지"), ("Why do they call you Dirge?", "왜 당신을 더지라고 부르지?"),
            ("Silver Platter", "은제 큰 접시"), ("Turn Undead", "언데드 퇴치"),
        });

        Assert.Equal(("moon sugar", "문 슈거"), Assert.Single(index.FindIn("Words sweeter than moon sugar.")));
        Assert.Equal("Words sweeter than __XT_TERM_N1_0000__.", index.ForceNames(new GlossaryApplication(
            "Words sweeter than moon sugar.", new Dictionary<string, string>(), Array.Empty<(string, string)>())).Text);
        Assert.Empty(index.FindIn("A sad dirge played. Served on a silver platter, bad enough to turn undead."));
    }

    // The memory names book series only by volume ("The Lusty Argonian Maid, v1" → "음란한 아르고니안 메이드, 제 1권"),
    // which is no name entry, so MEI wrote "Lusty Argonian Maid" as 음탕한 아르고니안 가정부. The title without the volume
    // is a name, with and without its leading "The".
    [Fact]
    public void BookTitlesWithoutTheirVolume_AreNames()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("The Lusty Argonian Maid, v1", "음란한 아르고니안 메이드, 제 1권"), ("The Lusty Argonian Maid, v2", "음란한 아르고니안 메이드, 제 2권"),
            ("The Real Barenziah, v1", "진정한 바렌자이아, 제 1권"),
        });

        Assert.Equal("음란한 아르고니안 메이드", Assert.Single(index.FindIn("I'm no Lusty Argonian Maid.")).Target);
        Assert.Equal("음란한 아르고니안 메이드", Assert.Single(index.FindIn("She has The Lusty Argonian Maid.")).Target);
        Assert.Equal("진정한 바렌자이아", Assert.Single(index.FindIn("A copy of The Real Barenziah.")).Target);
    }

    // Serana Dialogue Add-On's "Raven of the North" was matched as Raven (레이븐), a word the memory only ever writes in
    // "Raven Rock"; likewise Elder (Elder Scroll, Elder Council) and Ideal (Ideal Masters, "Ideal for my materials").
    // A word of a full name counts alone only where the memory also writes it alone ("Dagon has spoken"), and a one-word
    // name followed by another capitalized word is part of another name: Dagon Fel is a town, not 데이건.
    [Fact]
    public void WordsOnlyWrittenInsideLongerNames_AreNotNamesAlone()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Raven Rock", "레이븐 락"), ("Bring the note to a guard in Raven Rock", "레이븐 락의 경비병에게 노트를 전달하기"),
            ("Are there any other Imperials in Raven Rock?", "레이븐 락에 다른 임페리얼은 없나?"), ("A rock fell.", "바위가 떨어졌다."),
            ("Mehrunes Dagon", "메이룬스 데이건"), ("Altar of Mehrunes Dagon", "메이룬스 데이건의 제단"),
            ("Dagon has spoken.", "데이건의 명령이다."), ("I will use the Razor as I see fit, Dagon.", "이 면도칼은 내 마음대로 쓰겠소, 데이건."),
        });

        Assert.Empty(index.FindIn("Yeah, you can be Serana, Raven of the North!"));
        Assert.Equal("레이븐 락", Assert.Single(index.FindIn("Sail to Raven Rock.")).Target);
        Assert.Empty(index.FindIn("One in Dagon Fel, another in Black Marsh."));
        Assert.Equal(("Dagon", "데이건"), Assert.Single(index.FindIn("Hail, Lord Dagon.")));
    }

    // The rule above went too far on local projects: Kolbjorn (only in "Kolbjorn Barrow", 콜비욘 무덤) is a name, because
    // the memory translates the noun after it; and "Karthwasten River" or "Karthwasten Smelter" still name Karthwasten,
    // because a place noun follows. Only a whole name spelled by sound (Elder Scroll → 엘더 스크롤) or an unknown word
    // after the name (Dagon Fel) keeps the word out.
    [Fact]
    public void WordsBeforeATranslatedNounOrBeforeAPlaceNoun_AreNames()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Kolbjorn Barrow", "콜비욘 무덤"), ("Clear the Draugr from Kolbjorn Barrow", "콜비욘 무덤의 드로거를 처리하기"),
            ("Investigate Kolbjorn Barrow", "콜비욘 무덤을 조사하기"), ("An old barrow lies there.", "오래된 무덤이 있다."),
            ("Elder Scroll", "엘더 스크롤"), ("Read the Elder Scroll now", "지금 엘더 스크롤을 읽기"), ("Find the Elder Scroll", "엘더 스크롤 찾기"),
            ("Use the scroll.", "주문서를 사용한다."),
            ("Karthwasten", "카스웨이스튼"), ("I want to stop at Karthwasten.", "카스웨이스튼까지."),
        });

        Assert.Equal(("Kolbjorn", "콜비욘"), Assert.Single(index.FindIn("So far your investment into Kolbjorn hasn't paid.")));
        Assert.Empty(index.FindIn("It's what you call an Elder artifact."));
        Assert.Equal(("Karthwasten", "카스웨이스튼"), Assert.Single(index.FindIn("Treasure Map, Karthwasten River")));
    }

    // The glossary decides where its own terms are forced: the built-in Oblivion and Pale skip "What in Oblivion is this
    // place?" and "Pale light", and the name index forced them right after (오블리비언, 페일), bringing back the Serana bug.
    [Fact]
    public void TermsTheGlossaryHas_AreLeftToTheGlossary()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Oblivion", "오블리비언"), ("Close the gate to Oblivion", "오블리비언으로 가는 관문을 닫기"),
            ("The Pale", "페일"), ("Pale", "페일"), ("Travel to the Pale", "페일로 이동하기"),
        });
        var glossary = new GlossaryApplier(new[]
        {
            new GlossaryEntry(1, null, "Oblivion", "오블리비언", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, "Built-in default glossary"),
            new GlossaryEntry(2, null, "Pale", "페일", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, "Built-in default glossary"),
        });

        Assert.Equal("What in Oblivion is this place?", index.ApplyWithGlossary("What in Oblivion is this place?", glossary).Text);
        Assert.Equal("Pale light filled the room.", index.ApplyWithGlossary("Pale light filled the room.", glossary).Text);
    }

    // Ordinary words were forced at the start of a line: "Courage, my friend." → 고무 (the spell), "Cotton sheets" →
    // 코튼. Courage counted as named in sentences through item names ("Scroll of Courage"), and Cotton is an entry
    // spelled by sound that no sentence uses; such a word is only a name inside a sentence, where it is capitalized for
    // that reason. Names the memory's sentences use (Erandur) stay names at the start of a line.
    [Fact]
    public void WordsNamedOnlyInItemNamesOrByEntry_AreNotNamesAtTheStartOfALine()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Courage", "고무"), ("Scroll of Courage", "고무의 주문서"), ("Staff of Courage", "고무의 지팡이"),
            ("Cotton", "코튼"), ("Cairine", "카이린"),
            ("Erandur", "에란더"), ("Speak to Erandur", "에란더와 대화하기"),
        });

        Assert.Empty(index.FindIn("Courage, my friend."));
        Assert.Empty(index.FindIn("Cotton sheets are soft."));
        Assert.Equal("카이린", Assert.Single(index.FindIn("Have you met Cairine yet?")).Target);
        Assert.Equal("에란더", Assert.Single(index.FindIn("Erandur waits at the temple.")).Target);
    }

    // In lowercase only a name spelled by sound throughout is a name ("moon sugar", 문 슈거): "a suit of dragon armor"
    // was forced as the smithing perk Dragon Armor (드래곤 제련), "the seeker remains silent" as Seeker Remains.
    [Fact]
    public void LowercasePhrases_AreNamesOnlyWhenSpelledBySoundThroughout()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Moon Sugar", "문 슈거"), ("Deliver Moon Sugar to the caravan", "카라반에 문 슈거를 배달하기"),
            ("Dragon Armor", "드래곤 제련"), ("Seeker Remains", "시커의 잔재"),
        });

        Assert.Equal("문 슈거", Assert.Single(index.FindIn("Sweeter than moon sugar.")).Target);
        Assert.Empty(index.FindIn("A suit of dragon armor. The seeker remains silent."));
    }

    // A name is put back before the glossary only when a shorter glossary term broke it (Agent of Dibella). A longer
    // glossary term around the name is the glossary's: "Tiber Septim" (타이버 셉팀) came out as "[타이버] Septim".
    [Fact]
    public void ALongerGlossaryTermAroundAName_KeepsTheGlossarysTranslation()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Tiber", "타이버"), ("Speak to Tiber about it", "타이버에게 그것에 관해 말하기"),
            ("Raven Rock", "레이븐 락"), ("Sail to Raven Rock", "레이븐 락으로 항해하기"),
        });
        var glossary = new GlossaryApplier(new[]
        {
            new GlossaryEntry(1, null, "Tiber Septim", "타이버 셉팀", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
            new GlossaryEntry(2, null, "Raven Rock Harbor", "레이븐 락 항구", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 100, null),
        });

        var applied = index.ApplyWithGlossary("Tiber Septim united Tamriel near Raven Rock Harbor.", glossary);

        Assert.Equal(new[] { "레이븐 락 항구", "타이버 셉팀" }, applied.TokenToReplacement.Values.OrderBy(v => v, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("Septim", applied.Text);
    }

    // MEI's names that the memory has only inside sentences were left to the model: "Gray Quarter" (잿빛 지구),
    // "Lake Honrich" (혼리크 호수). A name the sentences translate the same way is a name; the particles after it are not.
    [Fact]
    public void NamesTheSentencesTranslateAlike_AreNames()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Any plans to renovate the Gray Quarter?", "잿빛 지구를 개선할 계획이 있습니까?"),
            ("What's the Gray Quarter?", "잿빛 지구가 뭡니까?"),
            ("You live in the Gray Quarter?", "잿빛 지구에서 살고 있나?"),
            ("How did it end up in Lake Honrich?", "어쩌다 혼리크 호수로 보내버린 거야?"),
            ("Locate the Quill of Gemination under Lake Honrich", "복제의 깃털펜을 혼리크 호수 바닥에서 찾기"),
        });

        Assert.Equal(("Gray Quarter", "잿빛 지구"), Assert.Single(index.FindIn("Meet me in the Gray Quarter.")));
        Assert.Equal(("Lake Honrich", "혼리크 호수"), Assert.Single(index.FindIn("The shore of Lake Honrich.")));
    }

    // Only names that stand alone in real sentences count: part of an item name ("Gloves of Major Destruction"), the
    // first words of a line ("Join Barbas"), a translation ending in a possessive ("King Olaf's Verse" is 올라프 왕의 시,
    // so "Olaf's Verse" is not 올라프 왕의), a name that longer official names write another way (the Guild Master's
    // armor is 길드 마스터의 방어구) and a name the sentences translate two ways are left alone.
    [Fact]
    public void NamesTheSentencesDoNotSettle_AreNotNames()
    {
        var index = ReferenceNameIndex.Build(new[]
        {
            ("Gloves of Major Destruction", "파괴마법 중급 강화 장갑"), ("Ring of Major Destruction", "파괴마법 중급 강화 반지"),
            ("Boots of Major Destruction", "파괴마법 중급 강화 전투화"),
            ("Join Barbas outside", "바깥에서 바바스와 합류하기"), ("Join Barbas at the gate", "성문에서 바바스와 합류하기"),
            ("King Olaf's Verse", "올라프 왕의 시"),
            ("I've been to the tomb. I have Olaf's Verse.", "그 무덤에 가본 적이 있고, 올라프 왕의 시도 갖고 있습니다."),
            ("Help Viarmo reconstruct Olaf's Verse", "비아모가 올라프 왕의 시를 복원하는 것을 돕기"),
            ("Bring it to Sunhallowed Arrows today", "태양의 신성함을 받은 화살에게 오늘 가져가기"),
            ("He sells Sunhallowed Arrows now", "그는 이제 태양의 신성함을 받은 화살을 판다"),
            ("Guild Master's Armor", "길드 마스터의 방어구"),
            ("Speak to Brynjolf about becoming the Guild Master", "브린욜프와 길드 지도자가 되는 것에 관해 대화하기"),
            ("Speak to Brynjolf about being Guild Master", "브린욜프에게 길드 지도자로 살아가는 것에 관해서 대화하기"),
        });

        Assert.Empty(index.FindIn("Major Destruction and Barbas."));
        Assert.Equal("올라프 왕의 시", Assert.Single(index.FindIn("Read King Olaf's Verse.")).Target);
        Assert.Empty(index.FindIn("I have Olaf's Verse and Sunhallowed Arrows."));
        Assert.DoesNotContain(index.FindIn("You are the Guild Master now."), name => name.Source == "Guild Master");
    }

    [Fact]
    public async Task NamesAreWrittenAsTheirOfficialTranslation_UnlessTheGlossaryForcesThem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-names-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await db.UpsertProjectAsync(new ProjectInfo(1, "C:\\dummy.xml", "Dummy", null, "english", "korean", "1", false,
                "<?xml version=\"1.0\"?>", "gemini-test", "base", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
            await db.BulkInsertStringsAsync(new[]
            {
                (OrderIndex: 1, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null, Edid: (string?)null,
                    Rec: (string?)"INFO:NAM1", SourceText: "Erandur's goddess Mara could stand up to Vaermina.", DestText: "",
                    Status: StringEntryStatus.Pending, RawStringXml: "<r/>"),
            }, CancellationToken.None);
            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            var handler = new PromptRecorder();
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            await service.TranslateIdsAsync(new TranslateIdsRequest(
                ApiKey: "DUMMY", ModelName: "gemini-test", SourceLang: "english", TargetLang: "korean", SystemPrompt: "base",
                Ids: ids, BatchSize: 10, MaxChars: 5000, MaxConcurrency: 1, Temperature: 0.2, MaxOutputTokens: 512, MaxRetries: 0,
                UseRecStyleHints: false, EnableRepairPass: false, EnableSessionTermMemory: false, OnRowUpdated: null, WaitIfPaused: null,
                CancellationToken: CancellationToken.None, EnablePromptCache: false,
                GlobalGlossary: new[]
                {
                    new GlossaryEntry(1, null, "Mara", "마라", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null),
                },
                ReferenceNameMemory: new[] { ("Erandur", "에란더"), ("Speak to Erandur", "에란더와 대화하기"), ("Mara", "마라 여신"), ("Pray to Mara", "마라에게 기도하기") }));

            var prompt = handler.Prompts.First();
            Assert.DoesNotContain("Erandur", prompt);
            var row = Assert.Single(await db.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal(StringEntryStatus.Done, row.Status);
            Assert.Contains("에란더", row.DestText);
            // Mara is forced by the glossary, so the glossary's translation wins over the memory's.
            Assert.Contains("마라", row.DestText);
            Assert.DoesNotContain("마라 여신", row.DestText);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    private sealed class PromptRecorder : HttpMessageHandler
    {
        public List<string> Prompts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var prompt = json.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
            Prompts.Add(prompt);

            // Echo the protection tokens so that the answer passes the token checks.
            static string Answer(string masked)
                => "여신 " + string.Join(" ", System.Text.RegularExpressions.Regex.Matches(masked, @"__XT_[A-Z0-9_]+?_[0-9]{4}__").Select(m => m.Value)) + " 이야기.";

            const string marker = "Input JSON:";
            var text = Answer(prompt);
            var index = prompt.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                using var input = JsonDocument.Parse(prompt[(index + marker.Length)..].Trim());
                var translations = input.RootElement.GetProperty("items").EnumerateArray()
                    .Select(item => new { id = item.GetProperty("id").GetInt64(), text = Answer(item.GetProperty("text").GetString() ?? "") })
                    .ToList();
                text = JsonSerializer.Serialize(new { translations });
            }

            var response = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } }, finishReason = "STOP" } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
