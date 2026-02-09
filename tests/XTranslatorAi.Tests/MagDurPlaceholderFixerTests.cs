using System;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public sealed class MagDurPlaceholderFixerTests
{
    [Fact]
    public void Fix_SwapsMagDur_WhenClearlySwapped_EvenWithCultureStyleLangCode()
    {
        var source = "Causes <mag> points of poison damage on non dwarven targets for <dur> seconds.";
        var dest = "드워프가 아닌 대상에게 <mag>초 동안 <dur>만큼의 독 피해를 입힙니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Contains("<dur>초", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<mag>초", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<mag>", fixedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fix_RewritesRegenTemplate_WhenMagPercentIsUsedAsTime()
    {
        var source = "Magicka regenerates <mag>% slower for <dur> seconds.";
        var dest = "매지카<mag>% 동안 <dur> 재생 속도가 초 만큼 느려집니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Equal("<dur>초 동안 매지카 재생 속도가 <mag>% 느려집니다.", fixedText);
    }

    [Fact]
    public void Fix_RewritesBurnAttackDamageToHealthEverySecondTemplate()
    {
        var source = "An attack which burns the enemy for 6 seconds, dealing <mag> points of damage to Health every second.";
        var dest = "적을 6초 동안 불태워, 매초 <mag> 수치에 체력포인트의 피해를 입히는 공격입니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Equal("대상을 6초 동안 불태워 초당 체력에 <mag>포인트의 피해를 줍니다.", fixedText);
    }

    [Fact]
    public void Fix_RewritesStreamOfColdDamagePerSecondTemplate()
    {
        var source = "A stream of cold that does <mag> points of damage per second to Health and Stamina.";
        var dest = "<mag>와(과) 체력에게 초당 지구력만큼의 냉기 피해를 주는 냉기 줄기를 발사합니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Contains("초당", fixedText, StringComparison.Ordinal);
        Assert.Contains("<mag>", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<mag>와", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("지구력만큼", fixedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fix_RewritesPoisonDamageOnNonDwarvenTargetsTemplate()
    {
        var source = "Causes <mag> points of poison damage on non dwarven targets for <dur> seconds.";
        var dest = "드워프가 아닌 대상에게 <mag>초 동안 <dur>만큼의 독 피해를 입힙니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Contains("<dur>초", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<mag>포인트", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<mag>초", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<dur>만큼", fixedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fix_RewritesRayOfFireDamagePerSecondExtraDamageTemplate()
    {
        var source = "A ray of fire that does <mag> points of damage per second. Targets on fire take extra damage.";
        var dest = "<mag>에게 초당 화염 피해를 주는 불꽃 광선입니다. 불타는 대상은 추가 피해를 받습니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Contains("초당", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<mag>", fixedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<mag>에게", fixedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fix_RewritesLightningBoltShockHalfMagickaLeapsTemplate()
    {
        var source = "Lightning bolt that does <mag> points of shock damage to Health and half to Magicka, then leaps to a new target.";
        var dest = "<mag>에게 체력포인트의 전격 피해를 입히고 매지카에게는 그 절반의 피해를 입힌 뒤, 새로운 대상에게 전이되는 전격 화살입니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Equal("체력에 <mag>포인트의 전격 피해를 주고, 매지카에는 그 절반의 피해를 준 뒤 새로운 대상에게 전이되는 번개 화살을 발사합니다.", fixedText);
    }

    [Fact]
    public void Fix_RewritesBoltOfLightningShockHalfMagickaTemplate()
    {
        var source = "A bolt of lightning that does <mag> points of shock damage to Health and half that to Magicka.";
        var dest = "<mag>에게 체력포인트의 전격 피해를 주고, 매지카에게는 그 절반의 피해를 주는 번개 화살을 발사합니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Equal("체력에 <mag>포인트의 전격 피해를 주고, 매지카에는 그 절반의 피해를 주는 번개 화살을 발사합니다.", fixedText);
    }

    [Fact]
    public void Fix_RewritesDrainsMagPointsFromStaminaTemplate()
    {
        var source = "Drains <mag> points from stamina.";
        var dest = "<mag>에서 지구력포인트를 흡수합니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Equal("지구력에서 <mag>포인트를 흡수합니다.", fixedText);
    }

    [Fact]
    public void Fix_RewritesLacerateBleedAttackTemplate()
    {
        var source = "An attack which lacerates the enemy causing it to bleed for 5 seconds, dealing <mag> points of damage to Health and Stamina every second.";
        var dest = "적을 찢어 5초 동안 출혈을 일으키며, <mag> 및 체력에게 초당 지구력포인트의 피해를 입히는 공격입니다.";

        var fixedText = MagDurPlaceholderFixer.Fix(source, dest, targetLang: "ko-KR");

        Assert.Equal("적을 찢어 5초 동안 출혈을 일으키며, 초당 체력과 지구력에 <mag>포인트의 피해를 줍니다.", fixedText);
    }
}
