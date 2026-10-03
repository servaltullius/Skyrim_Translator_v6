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
