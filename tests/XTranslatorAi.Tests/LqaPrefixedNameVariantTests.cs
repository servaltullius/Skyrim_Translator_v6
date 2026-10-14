using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The Elden War Ash mods name each skill in several records: "Learning - Messmer's Assault" (the effect that teaches
/// it), "Rim Scroll - Messmer's Assault" (the scroll), "Annotation - ..." and the spell itself. 134 rows in five mods
/// gave one skill two or three names ("메스메르의 강습" / "메스메르의 맹공"), and the same-source check missed them all
/// because the full source texts differ.
/// </summary>
public class LqaPrefixedNameVariantTests
{
    private static LqaScanEntry Row(long id, string rec, string source, string dest)
        => new(id, (int)id, "Edid" + id, rec, StringEntryStatus.Done, source, dest);

    private static List<LqaScanEntry> TemplateRows(params LqaScanEntry[] extra)
    {
        // Three other skills make "Learning -" and "Rim Scroll -" recognisable as name templates.
        var rows = new List<LqaScanEntry>
        {
            Row(101, "MGEF:FULL", "Learning - Weed Cutter", "배우기 - 잡초 베기"),
            Row(102, "SCRL:FULL", "Rim Scroll - Weed Cutter", "림 주문서 - 잡초 베기"),
            Row(103, "MGEF:FULL", "Learning - Wild Swing", "배우기 - 거친 휘두르기"),
            Row(104, "SCRL:FULL", "Rim Scroll - Wild Swing", "림 주문서 - 거친 휘두르기"),
            Row(105, "MGEF:FULL", "Learning - Tan Tui", "배우기 - 탄퇴"),
            Row(106, "SCRL:FULL", "Rim Scroll - Tan Tui", "림 주문서 - 탄퇴"),
        };
        rows.AddRange(extra);
        return rows;
    }

    [Fact]
    public async Task SkillNamedDifferentlyInTheScroll_IsNoted()
    {
        var entries = TemplateRows(
            Row(1, "MGEF:FULL", "Learning - Messmer's Assault", "배우기 - 메스메르의 강습"),
            Row(2, "SCRL:FULL", "Rim Scroll - Messmer's Assault", "림 주문서 - 메스메르의 맹공"));

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var noted = issues.Where(i => i.Code == "prefixed_name_variant").ToList();
        Assert.Equal(2, noted.Count);
        Assert.All(noted, i => Assert.Equal("Info", i.Severity));
        Assert.Contains("메스메르의 맹공", noted.Single(i => i.Id == 1).Message);
        Assert.Contains("메스메르의 강습", noted.Single(i => i.Id == 2).Message);
    }

    [Fact]
    public async Task SpellNameItself_DecidesTheMajority()
    {
        // The spell row is what the magic menu shows; the learning effect and the scroll should follow it.
        var entries = TemplateRows(
            Row(1, "MGEF:FULL", "Learning - Honed Bolt", "배우기 - 벼락"),
            Row(2, "SCRL:FULL", "Rim Scroll - Honed Bolt", "림 주문서 - 벼락"),
            Row(3, "SPEL:FULL", "Honed Bolt", "벼락 낙하"));

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var noted = issues.Where(i => i.Code == "prefixed_name_variant").OrderBy(i => i.Id).ToList();
        Assert.Equal(new long[] { 1, 2 }, noted.Select(i => i.Id));
        Assert.All(noted, i => Assert.Contains("벼락 낙하", i.Message));
    }

    [Fact]
    public async Task TrailingQualifier_IsLeftOutOfTheName()
    {
        // RimSkills: "Weapon Art - Ruinous Strike (Charged)" as 파멸의 일격 and "Waterfowl Dance (1H Sword)" as 물새 난무
        // never met their other rows, because "(Charged)" and "(1H Sword)" made them different names. War Ash 3 wrote
        // "(WIP)" right after the name.
        var entries = TemplateRows(
            Row(1, "MGEF:FULL", "Learning - Ruinous Strike", "배우기 - 파멸의 타격"),
            Row(2, "ENCH:FULL", "Weapon Art - Ruinous Strike", "전투 기술 - 파멸의 타격"),
            Row(3, "ENCH:FULL", "Weapon Art - Ruinous Strike (Charged)", "전투 기술 - 파멸의 일격 (차지)"),
            Row(4, "MGEF:FULL", "Ruinous Strike (Dual, Charged)", "파멸의 타격 (쌍수, 차지)"),
            Row(5, "MGEF:FULL", "Learning - Waterfowl Dance", "배우기 - 물새 난격"),
            Row(6, "SCRL:FULL", "Rim Scroll - Waterfowl Dance", "림 주문서 - 물새 난격"),
            Row(7, "MGEF:FULL", "Waterfowl Dance (1H Sword)", "물새 난무 (한손검)"),
            Row(8, "MGEF:FULL", "Learning - Time-Crack Resonance", "배우기 - 시간의 틈 공명"),
            Row(9, "SCRL:FULL", "Rim Scroll - Time-Crack Resonance(WIP)", "림 주문서 - 시간 균열 공명(제작 중)"),
            Row(10, "ENCH:FULL", "Weapon Art - Weed Cutter", "전투 기술 - 잡초 베기"),
            Row(11, "ENCH:FULL", "Weapon Art - Wild Swing (Charged)", "전투 기술 - 거친 휘두르기 (차지)"),
            // The qualifier is the only difference, so the name agrees.
            Row(12, "MGEF:FULL", "Wild Swing (Dual)", "거친 휘두르기 (쌍수)"));

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        var noted = issues.Where(i => i.Code == "prefixed_name_variant").OrderBy(i => i.Id).ToList();
        Assert.Equal(new long[] { 3, 7, 8, 9 }, noted.Select(i => i.Id));
        Assert.Contains("파멸의 타격", noted.Single(i => i.Id == 3).Message);
        Assert.Contains("물새 난격", noted.Single(i => i.Id == 7).Message);
    }

    [Fact]
    public async Task ConsistentNames_SpacingInsideTheTemplate_AndOneOffDashes_AreNotNoted()
    {
        var entries = TemplateRows(
            Row(1, "MGEF:FULL", "Learning - Raptor of the Mists", "배우기 - 안개 까마귀"),
            Row(2, "SCRL:FULL", "Rim Scroll - Raptor of the Mists", "림 주문서 -  안개 까마귀"),
            Row(3, "SPEL:FULL", "Raptor of the Mists", "안개 까마귀"),
            // A dash that is not a name template: only one row uses "Bandit Chief -".
            Row(4, "NPC_:FULL", "Bandit Chief - Fire", "산적 두목 - 화염"),
            Row(5, "MGEF:FULL", "Fire", "불"),
            // Dialogue is left out.
            Row(6, "INFO:NAM1", "Learning - Tan Tui", "배우기 - 담퇴"));

        var issues = await LqaScanner.ScanAsync(entries, "ko", new List<GlossaryEntry>());

        Assert.DoesNotContain(issues, i => i.Code == "prefixed_name_variant");
    }
}
