using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>Serana's review corrected 246 rows to the series TM's name; the quality check reported none of them.</summary>
public class LqaOfficialNameTests
{
    private static LqaScanEntry Row(long id, string rec, string source, string dest)
        => new(id, (int)id, "SDA_Topic", rec, StringEntryStatus.Done, source, dest);

    [Fact]
    public async Task DialogueWithoutTheOfficialName_IsReportedAsInformation()
    {
        var names = ReferenceNameIndex.Build(new[]
        {
            ("Jorrvaskr", "요르바스카"), ("Nightgate Inn", "나이트게이트 여관"),
            ("Meet me in Jorrvaskr.", "요르바스카에서 만나자."), // a single word must also be a name in a sentence
        });
        var entries = new List<LqaScanEntry>
        {
            Row(1, "INFO:NAM1", "Ah, Jorrvaskr. Good times.", "아, 요르바스크네. 좋은 시절이었지."),
            Row(2, "INFO:NAM1", "We could rest at the Nightgate Inn.", "나이트게이트 여관에서 쉬어도 돼."),
            Row(3, "WEAP:FULL", "Jorrvaskr Blade", "동료의 검"),                                   // item names are not checked
            Row(4, "INFO:NAM1", "Let's go to Jorrvaskr.", "조르바스크로 가자."),                    // the glossary decides
        };
        var glossary = new List<GlossaryEntry>();

        var issues = await LqaScanner.ScanAsync(entries.Take(3).ToList(), "ko", glossary, referenceNames: names);

        var issue = Assert.Single(issues, i => i.Code == "official_name_missing");
        Assert.Equal((1L, "Info"), (issue.Id, issue.Severity));
        Assert.Contains("요르바스카", issue.Message);

        glossary.Add(new GlossaryEntry(1, null, "Jorrvaskr", "조르바스크", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null));
        var withGlossary = await LqaScanner.ScanAsync(new[] { entries[3] }, "ko", glossary, referenceNames: names);
        Assert.DoesNotContain(withGlossary, i => i.Code == "official_name_missing");
    }

    /// <summary>
    /// Meridia's Order's "Jester's Boots" was written as the game names it, 광대의 신발, and the check flagged the
    /// built-in Boots => 전투화 as missing. A term inside a name the TM spells otherwise is the name's; outside one it
    /// is still flagged.
    /// </summary>
    [Fact]
    public async Task GlossaryTermInsideATmName_SpelledAsTheTmHasIt_IsNotMissing()
    {
        var names = ReferenceNameIndex.Build(new[] { ("Jester's Boots", "광대의 신발"), ("Jester's Gloves", "광대의 장갑"), ("Boots", "신발") });
        var glossary = new List<GlossaryEntry>
        {
            new(1, null, "Boots", "전투화", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, "Built-in default glossary"),
        };
        var entries = new List<LqaScanEntry>
        {
            Row(1, "ARMO:FULL", "Jester's Boots", "광대의 신발"),
            Row(2, "ARMO:FULL", "Iron Boots", "철 신발"),
            Row(3, "BOOK:DESC", "He wore Jester's Boots and Iron Boots.", "그는 광대의 신발과 철 신발을 신었다."),
            // A TM name that is the term itself does not overrule the glossary.
            Row(4, "INFO:NAM1", "Nice Boots.", "멋진 신발이네."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", glossary, referenceNames: names);

        Assert.Equal(new long[] { 2, 3, 4 }, issues.Where(i => i.Code == "glossary_missing").Select(i => i.Id).OrderBy(i => i));
    }
}
