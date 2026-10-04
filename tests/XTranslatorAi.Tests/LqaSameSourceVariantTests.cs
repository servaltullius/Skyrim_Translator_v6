using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaSameSourceVariantTests
{
    private static LqaScanEntry Row(long id, string rec, string source, string dest)
        => new(id, (int)id, "Edid" + id, rec, StringEntryStatus.Done, source, dest);

    [Fact]
    public async Task MinorityTranslationOfARepeatedName_IsNoted_DialogueIsNot()
    {
        var entries = new List<LqaScanEntry>
        {
            Row(1, "PERK:FULL", "Fortify Mystic", "신비 강화"),
            Row(2, "PERK:FULL", "Fortify Mystic", "신비 강화"),
            Row(3, "PERK:FULL", "Fortify Mystic", "강화 신비"),
            Row(4, "SPEL:FULL", "Fortify Mystic", "신비 증폭"),       // another record type
            Row(5, "INFO:NAM1", "Follow me.", "따라와."),
            Row(6, "INFO:NAM1", "Follow me.", "따라오세요."),          // dialogue may differ by scene
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "same_source_variant");
        Assert.Equal((3L, "Info"), (issue.Id, issue.Severity));
        Assert.Contains("신비 강화", issue.Message);
    }
}
