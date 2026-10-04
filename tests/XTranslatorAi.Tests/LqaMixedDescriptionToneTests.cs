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
}
