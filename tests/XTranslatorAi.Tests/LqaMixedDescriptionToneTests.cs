using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaMixedDescriptionToneTests
{
    private static LqaScanEntry Row(long id, string rec, string dest)
        => new(id, (int)id, "Edid" + id, rec, StringEntryStatus.Done, "Source text.", dest);

    [Fact]
    public async Task DescriptionMixingHamnidaAndPlainDa_IsReported()
    {
        var entries = new List<LqaScanEntry>
        {
            Row(1, "MGEF:DNAM", "상식을 벗어난 기술입니다. 연속 베기 공격을 빠르게 퍼붓는다."),
            Row(2, "SPEL:DESC", "적에게 피해를 줍니다. 대검에 사용할 수 있습니다."),       // all 합니다체
            Row(3, "MGEF:DNAM", "재사용 대기시간 15초. 피해를 줍니다."),                    // a label is not 해라체
            Row(4, "BOOK:DESC", "그는 떠났습니다. 그리고 돌아오지 않았다."),                // books have their own check
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 1 }, issues.Where(i => i.Code == "mixed_description_tone").Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task DescriptionClosingHamnidaWithSayongGaneung_IsReported()
    {
        // War Ash 2: 48 of 50 descriptions closed "…시작할 수 있습니다. 모든 무기에 사용 가능." while War Ash 1 and 3
        // wrote "사용할 수 있습니다."; the description style asks for 합니다체 in every sentence.
        var entries = new List<LqaScanEntry>
        {
            Row(1, "MGEF:DNAM", "상식을 벗어난 기술입니다. 도약 발차기로 공격합니다. 모든 무기에 사용 가능."),
            Row(2, "SPEL:DESC", "강력한 일격을 날립니다. 방패로는 사용 불가."),
            Row(3, "MGEF:DNAM", "상식을 벗어난 기술입니다. 근접 무기에 사용 가능합니다."),   // 합니다체
            Row(4, "MGEF:DNAM", "한손무기 및 양손무기 적용. 피해량이 50% 증가합니다."),       // a label, not a closing noun
            Row(5, "ARMO:DESC", "착용 가능"),                                              // no 합니다체 sentence to mix with
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var mixed = issues.Where(i => i.Code == "mixed_description_tone").OrderBy(i => i.Id).ToList();
        Assert.Equal(new long[] { 1, 2 }, mixed.Select(i => i.Id).ToArray());
        Assert.Contains("사용 가능", mixed[0].Message);
    }
}
