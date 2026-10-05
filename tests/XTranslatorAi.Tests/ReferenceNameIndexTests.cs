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
