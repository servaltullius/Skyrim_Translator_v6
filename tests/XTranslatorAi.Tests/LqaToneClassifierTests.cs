using System.Collections.Generic;
using XTranslatorAi.Core.Text.Lqa.Internal;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaToneClassifierTests
{
    [Theory]
    [InlineData("정리합니다.", "Hamnida")]
    [InlineData("이제 가요.", "Haeyo")]
    [InlineData("이것은 기록이다.", "PlainDa")]
    [InlineData("어서 와.", "Unknown")]
    public void Classify_ReturnsExpectedTone(string text, string expected)
    {
        var actual = LqaToneClassifier.Classify(text);
        Assert.Equal(expected, actual.ToString());
    }

    [Fact]
    public void TryGetStrongMajorityTone_ReturnsMajority_WhenThresholdSatisfied()
    {
        var tones = new List<ToneKind>
        {
            ToneKind.Hamnida,
            ToneKind.Hamnida,
            ToneKind.Hamnida,
            ToneKind.Haeyo,
        };

        var ok = LqaToneClassifier.TryGetStrongMajorityTone(tones, out var majority);

        Assert.True(ok);
        Assert.Equal(ToneKind.Hamnida, majority);
    }
}
