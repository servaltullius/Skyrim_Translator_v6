using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaHiddenTopicTests
{
    private static LqaScanEntry Row(long id, string rec, string source, string dest)
        => new(id, (int)id, "SDA_Topic", rec, StringEntryStatus.Done, source, dest);

    [Fact]
    public async Task TranslatedHiddenTopic_IsReported_OtherIdentifiersAreNot()
    {
        var entries = new List<LqaScanEntry>
        {
            Row(1, "DIAL:FULL", "SDA_OPResponse2", "SDA_OP반응2"),
            Row(2, "DIAL:FULL", "SDA_DA09IntroTopic00", "방금 저 빛나는 돌이 너한테 소리 지른 거야?"),
            Row(3, "DIAL:FULL", "SDA_HobbiesTopic", "SDA_HobbiesTopic"),          // kept as it is
            Row(4, "DIAL:FULL", "What do you think?", "어떻게 생각해?"),          // a visible topic
            Row(6, "DIAL:FULL", "LoNier.", "로니어."),                            // a name
            Row(5, "MGEF:FULL", "SDA_CellTrackMGEFTG", "셀 추적 효과"),           // effect names are translated on purpose
        };

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.Equal(new long[] { 1, 2 }, issues.Where(i => i.Code == "hidden_topic_translated").Select(i => i.Id).ToArray());
    }
}
