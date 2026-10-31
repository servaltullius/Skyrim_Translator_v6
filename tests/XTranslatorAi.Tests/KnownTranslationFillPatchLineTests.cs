using System;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

// The third review of the Korean patch filled its lines through the translator's fixes and found four the fixes broke or
// refused (2026-10-31): a word drawn out for effect, a demonstrative after an alias's marker, a number the source uses
// for damage and for seconds, and a sign said in words. The patch's line is filled as it is.
public class KnownTranslationFillPatchLineTests
{
    [Theory]
    [InlineData("I'm in the mood to ssssiiiiinnnggg! What about you, friend? Care to join me?",
        "노래 부르기 조오오오오오오오은 날이군! 어떤가, 친구? 같이 부를텐가?")]
    [InlineData("<Alias.ShortName=QuestGiver> gave me this ring. I think it's yours.",
        "<Alias.ShortName=QuestGiver>이(가) 이 반지를 주었는데, 당신 것 같군.")]
    [InlineData("Targets take <15> points of frost damage and <30> points of Stamina damage. Weaker creatures and people flee for <30> seconds.",
        "<15> 의 냉기 피해와 <30> 의 지구력 피해를 입힙니다. 약한 생물과 사람은 <30> 초 동안 도망칩니다.")]
    [InlineData("+<mag> Speechcraft.", "화술이 <mag> 증가합니다.")]
    [InlineData("Resist Frost +<mag>% and Resist Fire -<mag>%.", "냉기 저항이 <mag>% 증가하고 화염 저항이 <mag>% 감소합니다.")]
    // The patch writes a placeholder's seconds apart; 322 of its lines were filled glued ("<dur>초 동안").
    [InlineData("For <dur> seconds you automatically become invisible while sneaking.", "<dur> 초 동안 은신 시 투명화합니다.")]
    public void PatchLine_IsFilledAsItIs(string source, string patch)
    {
        var prepared = KnownTranslationFill.Prepare(1, source, patch, "korean", enableTemplateFixer: true);

        Assert.Null(prepared.Failure);
        Assert.Equal(patch, prepared.Text);
    }

    [Fact]
    public void AMinusSaidAsLess_IsFilled()
        => TokenValidator.ValidateFinalTextIntegrity("Shout timer -<20>%.", "함성의 재사용 대기시간이 <20>% 감소합니다.", "test");

    [Theory]
    // The sign the source has may be said in words, not turned into the other one or written where the source has none.
    [InlineData("+<mag> Speechcraft.", "화술 -<mag>.")]
    [InlineData("Speechcraft is increased by <mag>.", "화술 +<mag>.")]
    // A minus left out with nothing saying less reads as a gain.
    [InlineData("-<mag> Speechcraft.", "화술 <mag>.")]
    [InlineData("Shout timer -<20>%.", "함성의 재사용 대기시간 <20>%.")]
    // The value itself must still be there.
    [InlineData("+<mag> Speechcraft.", "화술이 증가합니다.")]
    [InlineData("+<mag> Speechcraft, +<mag> Archery.", "화술과 궁술이 <mag> 증가합니다.")]
    public void LostOrChangedValue_IsStillRefused(string source, string translated)
        => Assert.ThrowsAny<InvalidOperationException>(() => TokenValidator.ValidateFinalTextIntegrity(source, translated, "test"));
}
