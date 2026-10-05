using System;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

// "N percent" written as a word is naturally translated as "N%". The final check treated
// that "N%" as unexpected protected text and failed the row (3 of 4 error rows in the
// 2026-09-30 quality evaluation).
public class PercentWordValidationTests
{
    [Theory]
    [InlineData("Increase all skill experience gain by 1 percent for each piece.", "세트 하나당 모든 기술 경험치 획득량이 1% 증가합니다.")]
    [InlineData("Resist disease increased 50 percent and damage to vampires increased 10 percent.", "질병 저항이 50% 증가하고 흡혈귀에게 주는 피해가 10% 증가합니다.")]
    [InlineData("additional 5 percent spell damage and 20 percent magicka regeneration", "추가 주문 피해 5%와 매지카 재생 20%")]
    [InlineData("Gain 5 per cent more gold.", "금을 5% 더 얻습니다.")]
    [InlineData("Increases damage by 50%.", "피해가 50퍼센트 증가합니다.")]
    [InlineData("+10% damage", "피해 10% 증가")]
    [InlineData("Reduces cost by 12.5 percent.", "비용이 12.5% 감소합니다.")]
    // Feris spells the number as well: "Seventy percent now." came back as "이제 70%야." and failed with E330.
    [InlineData("Seventy percent now.", "이제 70%야.")]
    [InlineData("Sixty-five percent.", "65%.")]
    [InlineData("Only a hundred percent will do, not twenty five percent.", "100%여야 해, 25%로는 안 돼.")]
    public void PercentWrittenInEitherForm_IsAccepted(string source, string translated)
        => TokenValidator.ValidateFinalTextIntegrity(source, translated, "test");

    [Theory]
    [InlineData("Increase by 1 percent.", "2% 증가합니다.")]
    [InlineData("Increases damage by 50%.", "피해가 5% 증가합니다.")]
    [InlineData("Increase by 50 percent.", "50% 증가하고 10% 추가됩니다.")]
    [InlineData("Increase by 50 percent and 10 percent.", "50% 증가합니다.")]
    [InlineData("Seventy percent now.", "이제 60%야.")]
    public void ChangedOrMissingPercentValue_IsRejected(string source, string translated)
        => Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateFinalTextIntegrity(source, translated, "test"));
}
