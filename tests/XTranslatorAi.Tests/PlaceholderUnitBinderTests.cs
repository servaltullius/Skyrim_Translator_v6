using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Tests;

public sealed class PlaceholderUnitBinderTests
{
    [Fact]
    public void InjectUnitsForTranslation_ReturnsSourceUnchanged_ForKorean()
    {
        var src =
            "Targets take <mag> points of frost damage for <dur> seconds, plus Stamina damage. "
            + "A blast of cold that does <mag> points of damage per second to Health and Stamina.";

        var injected = PlaceholderUnitBinder.InjectUnitsForTranslation("korean", src);

        Assert.Equal(src, injected);
    }

    [Fact]
    public void ReplaceUnitsAfterUnmask_ReplacesSyntheticTags_WithKoreanUnits_AndTightensSpacing()
    {
        var text = "for <dur> <XT_SEC> and <mag> <XT_PT> <XT_PER_SEC>";
        var replaced = PlaceholderUnitBinder.ReplaceUnitsAfterUnmask("ko-KR", text);
        Assert.Equal("for <dur>초 and <mag>포인트 초당", replaced);
    }

    [Fact]
    public void EnforceUnitsFromSource_AddsSeconds_AfterPlaceholders_AndTightensSpacing()
    {
        var source = "Targets take <mag> points of damage for <dur> seconds.";
        var dest = "<mag> <dur> 동안 피해를 입힙니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko-KR", source, dest);

        Assert.Equal("<mag> <dur>초 동안 피해를 입힙니다.", enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_HandlesNumericAnglePlaceholders_WithoutInjectingPoints()
    {
        var source = "Restore <40> points of Stamina for <15> seconds.";
        var dest = "<40> <15> 동안 지구력을 회복합니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("korean", source, dest);

        Assert.Equal("<40> <15>초 동안 지구력을 회복합니다.", enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_RemovesPointsAfterPlaceholder_WhenSourceDoesNotUsePoints()
    {
        var source = "Inflicts <mag> Health and Stamina damage against mortal creatures.";
        var dest = "필멸의 생명체에게 <mag>포인트의 체력 및 지구력 피해를 입힙니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko-KR", source, dest);

        Assert.Equal("필멸의 생명체에게 <mag>의 체력 및 지구력 피해를 입힙니다.", enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_DoesNotRemovePointsAfterPlaceholder_WhenSourceUsesPoints()
    {
        var source = "Restore <40> points of Stamina.";
        var dest = "<40>포인트 지구력을 회복합니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko-KR", source, dest);

        Assert.Equal(dest, enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_DoesNotDuplicateSeconds_WhenSeparatedByZeroWidthSpace()
    {
        var source = "Target is paralyzed for <dur> seconds.";
        var dest = "<dur> \u200B초 동안 마비시킵니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko-KR", source, dest);

        Assert.Equal("<dur>초 동안 마비시킵니다.", enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_AddsPerSecond_AsChodang_WhenMissing()
    {
        var source = "A ray of fire that does <mag> points of damage per second.";
        var dest = "<mag>의 화염 피해를 줍니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko-KR", source, dest);

        Assert.Equal("초당 <mag>의 화염 피해를 줍니다.", enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_AddsPerSecond_ForNumericMagnitudeToken()
    {
        var source = "For <150> seconds, you absorb your opponent's health within melee range, dealing <8> points of damage per second.";
        var dest = "<150>초 동안 근접한 적에게 <8>의 피해를 줍니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko", source, dest);

        Assert.Equal("<150>초 동안 근접한 적에게 초당 <8>의 피해를 줍니다.", enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_DoesNotDuplicatePerSecond_WhenAlreadyPresent()
    {
        var source = "A ray of fire that does <mag> points of damage per second.";
        var dest = "초당 <mag>포인트의 화염 피해를 줍니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("ko-KR", source, dest);

        Assert.Equal(dest, enforced);
    }

    [Fact]
    public void EnforceUnitsFromSource_DoesNothing_ForNonKorean()
    {
        var source = "Targets take <mag> points of damage for <dur> seconds.";
        var dest = "<mag> <dur> 동안 피해를 입힙니다.";

        var enforced = PlaceholderUnitBinder.EnforceUnitsFromSource("english", source, dest);

        Assert.Equal(dest, enforced);
    }
}
