using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaForeignScriptResidueTests
{
    private static LqaScanEntry Row(long id, string source, string dest)
        => new(id, (int)id, "SDA_Topic", "INFO:NAM1", StringEntryStatus.Done, source, dest);

    [Fact]
    public async Task KanaOrHanjaNotInSource_IsReported()
    {
        var entries = new List<LqaScanEntry>
        {
            Row(1, "I've never seen a Spellbreaker.", "スペルブレイカー를 본 적이 없어서."),
            Row(2, "A skill beyond the reach of most.", "범인(凡人)의 경지를 넘어선 기술."),
            Row(3, "A plain line.", "평범한 문장."),
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var found = issues.Where(i => i.Code == "foreign_script_residue").ToList();
        Assert.Equal(new long[] { 1, 2 }, found.Select(i => i.Id).ToArray());
        Assert.Contains("スペルブレイカー", found[0].Message);
    }

    [Fact]
    public async Task ChineseSource_IsNotReported()
    {
        var entries = new List<LqaScanEntry> { Row(1, "战技-动作执行", "전기-동작 실행(动作)") };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code == "foreign_script_residue");
    }
}
