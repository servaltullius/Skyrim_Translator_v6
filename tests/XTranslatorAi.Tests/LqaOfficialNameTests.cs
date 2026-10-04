using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>Serana's review corrected 246 rows to the official name; the quality check reported none of them.</summary>
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
}
