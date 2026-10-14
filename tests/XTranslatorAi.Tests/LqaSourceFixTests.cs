using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// A row marked "원문 오류 수정" was translated against its English on purpose: the modder pasted another record's
/// text ("Rim Scroll - Sudden Tornado" on a stagger effect). The check no longer compares it with its English or with
/// other rows, and other rows are not compared with it; its tags are still checked.
/// </summary>
public class LqaSourceFixTests
{
    private static LqaScanEntry Row(long id, string rec, string source, string dest)
        => new(id, (int)id, "Edid" + id, rec, StringEntryStatus.Done, source, dest);

    private static readonly GlossaryEntry RimScroll = new(1, "eldenrim", "Rim Scroll", "림 주문서", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 20, null);

    private static List<LqaScanEntry> Rows() => new()
    {
        Row(1, "MGEF:FULL", "Rim Scroll - Sudden Tornado", "약한 충격: 비틀거림 (한손)"),
        Row(2, "MGEF:FULL", "Rim Scroll - Sudden Tornado", "림 주문서 - 급선풍"),
        Row(3, "SPEL:DESC", "Deals <mag> damage.", "<mag>의 피해를 줍니다."),
        Row(4, "SPEL:DESC", "Deals <mag> damage.", "피해를 줍니다."),
    };

    [Fact]
    public async Task UnmarkedRows_AreFlagged()
    {
        var issues = await LqaScanner.ScanAsync(Rows(), "korean", new[] { RimScroll });

        Assert.Contains(issues, i => i.Id == 1 && i.Code == "glossary_missing");
        Assert.Contains(issues, i => i.Id == 2 && i.Code == "same_source_variant");
    }

    [Fact]
    public async Task MarkedRows_KeepOnlyTheirTagChecks()
    {
        var issues = await LqaScanner.ScanAsync(Rows(), "korean", new[] { RimScroll }, sourceFixIds: new HashSet<long> { 1, 4 });

        Assert.DoesNotContain(issues, i => i.Id == 1);
        Assert.DoesNotContain(issues, i => i.Id == 2 && i.Code == "same_source_variant");
        Assert.DoesNotContain(issues, i => i.Id == 3 && i.Code == "same_source_variant");
        Assert.Contains(issues, i => i.Id == 4 && i.Code == "token_mismatch");
    }
}
