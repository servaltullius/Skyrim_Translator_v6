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
/// Serana Dialogue Add-On spelled its player names one way in greetings and another in lines (드렐로레아 and
/// 드렐로리아, 벨레트 and 벨레스); a run now keeps the first spelling of a name for the rest of its rows.
/// </summary>
public class RunNameMemoryTests
{
    private static readonly string[] Sources =
    {
        "Hey Drelorea!",
        "Drelorea, are you okay?",
        "I'm sorry Byleth, I never should have asked.",
        "Byleth.",
        // A capitalized word also used in lowercase is an ordinary word; one only at the start of a sentence is not a name.
        "Ghost! A ghost appeared.",
        "Maybe we should go.",
        "Swims-In-Shadows.",
    };

    [Fact]
    public void FindsNamesUsedInSentencesAloneOrInGreetings()
    {
        var memory = RunNameMemory.Build(Sources);

        memory.Preload(new[] { ("Hey Drelorea!", "안녕, 드렐로레아!"), ("Byleth.", "벨레트."), ("Ghost! A ghost appeared.", "고스트! 유령이 나타났어."), ("Maybe we should go.", "메이비, 가자.") });

        Assert.Equal(new[] { "Byleth", "Drelorea" }, memory.Spellings.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("드렐로레아", memory.Spellings["Drelorea"]);
    }

    // A word the glossary has is the glossary's to force or leave ("Pale light" is not the Pale, 페일).
    [Fact]
    public void WordsTheGlossaryHas_AreNotRemembered()
    {
        var memory = RunNameMemory.Build(new[] { "The people of the Pale are tough.", "Hey Drelorea!" }, term => term == "Pale");

        memory.Preload(new[] { ("The people of the Pale are tough.", "페일 사람들은 강인해."), ("Hey Drelorea!", "안녕, 드렐로레아!") });

        Assert.Equal(new[] { "Drelorea" }, memory.Spellings.Keys.ToArray());
    }

    [Theory]
    [InlineData("Drelorea", "드렐로리아! 괜찮아?", "드렐로리아")]
    [InlineData("Drelorea", "드렐로레아는 나를 위해 많은 걸 해줬어.", "드렐로레아")]
    [InlineData("Byleth", "미안해, 벨레스. 괜히 물어봤네.", "벨레스")]
    [InlineData("Byleth", "미안해, 괜히 물어봤네.", null)]
    [InlineData("Swims", "그림자 속을 헤엄치는 자.", null)]
    // A word with no consonant sound at all (으으, 아아) failed the whole row with IndexOutOfRangeException (Feris AE).
    [InlineData("Drelorea", "으으, 추워... 드렐로레아는 어디 갔지?", "드렐로레아")]
    [InlineData("Byleth", "아아, 미안해.", null)]
    public void FindsTheKoreanWordThatSoundsLikeTheName(string name, string dest, string? expected)
        => Assert.Equal(expected, RunNameMemory.FindSpelling(name, dest));

    [Fact]
    public void PreloadTakesTheMostCommonSpelling_AndLearningKeepsTheFirst()
    {
        var memory = RunNameMemory.Build(Sources);
        memory.Preload(new[]
        {
            ("Hey Drelorea!", "안녕, 드렐로레아!"),
            ("Drelorea, are you okay?", "드렐로리아, 괜찮아?"),
            ("Drelorea!", "드렐로레아!"),
        });
        memory.Learn("I'm sorry Byleth, I never should have asked.", "미안해, 벨레스. 괜히 물어봤네.");
        memory.Learn("Byleth.", "벨레트.");

        Assert.Equal("드렐로레아", memory.Spellings["Drelorea"]);
        Assert.Equal("벨레스", memory.Spellings["Byleth"]);
    }

    [Fact]
    public void ForcesRememberedNamesAsTermTokens_ButNotInsideHyphenatedNames()
    {
        var memory = RunNameMemory.Build(Sources.Append("I asked Swims about it."));
        memory.Preload(new[] { ("Hey Drelorea!", "안녕, 드렐로레아!"), ("I asked Swims about it.", "스윔스에게 물어봤어.") });
        Assert.Equal("스윔스", memory.Spellings["Swims"]);

        var forced = memory.Force(new GlossaryApplication("Drelorea's sword and Swims-In-Shadows, __XT_PH_0000__ Drelorea.",
            new Dictionary<string, string>(), Array.Empty<(string, string)>()));

        var token = Assert.Single(forced.TokenToReplacement).Key;
        Assert.Equal($"{token}'s sword and Swims-In-Shadows, __XT_PH_0000__ {token}.", forced.Text);
        Assert.Equal("드렐로레아", forced.TokenToReplacement[token]);
    }

    [Theory]
    [InlineData("And whatever is left of Jack the Ripper we will face.", false)]
    [InlineData("You are not the Ripper of Whiterun.", false)]
    [InlineData("They called me the Ripper.", true)]
    [InlineData("Ripper. That was my name.", true)]
    [InlineData("Meet me at the Bannered Ripper tonight.", false)]
    public void KeepsLongerNamesTogether(string text, bool forced)
    {
        var memory = RunNameMemory.Build(new[] { "They called me the Ripper." });
        memory.Preload(new[] { ("They called me the Ripper.", "다들 날 리퍼라고 불렀지.") });

        var applied = memory.Force(new GlossaryApplication(text, new Dictionary<string, string>(), Array.Empty<(string, string)>()));

        Assert.Equal(forced, applied.TokenToReplacement.Count == 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaterRowsOfARunGetTheSpellingOfTheFirst(bool enabled)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-runnames-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await db.UpsertProjectAsync(new ProjectInfo(1, "C:\\dummy.xml", "Dummy", null, "english", "korean", "1", false,
                "<?xml version=\"1.0\"?>", "gemini-test", "base", null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
            await db.BulkInsertStringsAsync(new[]
            {
                Row(1, "Hey Drelorea!"),
                Row(2, "Ask Drelorea about the ship."),
            }, CancellationToken.None);
            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            var handler = new NameSpeller();
            var service = new TranslationService(db, new GeminiClient(new HttpClient(handler)));

            await service.TranslateIdsAsync(new TranslateIdsRequest(
                ApiKey: "DUMMY", ModelName: "gemini-test", SourceLang: "english", TargetLang: "korean", SystemPrompt: "base",
                Ids: ids, BatchSize: 1, MaxChars: 5000, MaxConcurrency: 1, Temperature: 0.2, MaxOutputTokens: 512, MaxRetries: 0,
                UseRecStyleHints: false, EnableRepairPass: false, EnableSessionTermMemory: false, OnRowUpdated: null, WaitIfPaused: null,
                CancellationToken: CancellationToken.None, EnablePromptCache: false, EnableDialogueContextWindow: false,
                EnableRunNameMemory: enabled));

            // The fake model spells the name differently each time; with the memory the second row gets the first spelling.
            var rows = await db.GetStringsAsync(10, 0, CancellationToken.None);
            Assert.All(rows, r => Assert.Equal(StringEntryStatus.Done, r.Status));
            Assert.Contains("드렐로레아", rows[0].DestText);
            Assert.Contains(enabled ? "드렐로레아" : "드렐로리아", rows[1].DestText);
            Assert.Equal(enabled, !handler.Prompts[1].Contains("Drelorea", StringComparison.Ordinal));
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    private static (int OrderIndex, string? ListAttr, string? PartialAttr, string? AttributesJson, string? Edid, string? Rec, string SourceText,
        string DestText, StringEntryStatus Status, string RawStringXml) Row(int order, string source)
        => (order, null, null, null, null, "INFO:NAM1", source, "", StringEntryStatus.Pending, "<r/>");

    private sealed class NameSpeller : HttpMessageHandler
    {
        public List<string> Prompts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var prompt = json.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
            Prompts.Add(prompt);

            string Answer(string masked)
            {
                var tokens = string.Join(" ", System.Text.RegularExpressions.Regex.Matches(masked, @"__XT_[A-Z0-9_]+?_[0-9]{4}__").Select(m => m.Value));
                var name = masked.Contains("Drelorea", StringComparison.Ordinal) ? (Prompts.Count == 1 ? "드렐로레아" : "드렐로리아") : "";
                return $"{name} {tokens} 이야기.".Trim();
            }

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
