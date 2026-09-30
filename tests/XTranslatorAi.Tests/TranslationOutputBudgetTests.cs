using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class TranslationOutputBudgetTests
{
    [Theory]
    [InlineData(65536)]
    [InlineData(3073)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Disabled_DoesNotChangeConfiguredValues(int configured)
    {
        Assert.Equal(configured, TranslationOutputBudget.Compute(int.MaxValue, int.MaxValue, int.MaxValue, configured, false));
        Assert.Equal(configured, TranslationOutputBudget.GetSourceCharLimit(configured, 0, false));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 3, 2)]
    [InlineData(-100, -3, -2)]
    public void ShortOrNegativeInput_UsesConservativeFloor(int chars, int tokens, int rows)
    {
        Assert.Equal(4096, TranslationOutputBudget.Compute(chars, tokens, rows, 65536, true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(4095)]
    [InlineData(5001)]
    [InlineData(65536)]
    public void ManualCeiling_IsHonoredEvenBelowFloorOrBetweenIncrements(int ceiling)
    {
        var shortBudget = TranslationOutputBudget.Compute(0, 0, 0, ceiling, true);
        Assert.InRange(shortBudget, 1, ceiling);
        Assert.Equal(Math.Min(4096, ceiling), shortBudget);
        Assert.Equal(ceiling, TranslationOutputBudget.Compute(int.MaxValue, int.MaxValue, int.MaxValue, ceiling, true));
    }

    [Fact]
    public void GrowingSource_NeverReducesBudget_AndEventuallySaturates()
    {
        var previous = 0;
        foreach (var chars in new[] { 0, 100, 1000, 2000, 4000, 8000, 16000, 32000, int.MaxValue })
        {
            var budget = TranslationOutputBudget.Compute(chars, 4, 12, 65536, true);
            Assert.InRange(budget, previous, 65536);
            Assert.Equal(0, budget % 256);
            previous = budget;
        }
        Assert.Equal(65536, previous);
    }

    [Fact]
    public void ProtectedTokensAndRows_ReceiveAdditionalRoom()
    {
        var plain = TranslationOutputBudget.Compute(4000, 0, 1, 65536, true);
        var tags = TranslationOutputBudget.Compute(4000, 50, 1, 65536, true);
        var moreRows = TranslationOutputBudget.Compute(4000, 50, 20, 65536, true);

        Assert.True(tags > plain);
        Assert.True(moreRows > tags);
        Assert.Equal(0, moreRows % 256);
    }

    [Fact]
    public void ExtremeInputs_DoNotOverflowIntoSmallOrNegativeBudgets()
    {
        Assert.Equal(int.MaxValue, TranslationOutputBudget.Compute(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, true));
        Assert.Equal(4096, TranslationOutputBudget.Compute(int.MinValue, int.MinValue, int.MinValue, int.MaxValue, true));
        Assert.Equal(1073740799, TranslationOutputBudget.GetSourceCharLimit(int.MaxValue, int.MaxValue, true));
    }

    [Theory]
    [InlineData(15000, 65536, 15000)]
    [InlineData(15000, 16384, 7168)]
    [InlineData(15000, 4096, 1024)]
    [InlineData(15000, 1024, 256)]
    [InlineData(100, 4096, 100)]
    [InlineData(0, 4096, 0)]
    [InlineData(-1, 4096, 0)]
    public void SourceLimit_RespectsConfiguredMaximumAndMinimumChunk(int maxChars, int ceiling, int expected)
    {
        Assert.Equal(expected, TranslationOutputBudget.GetSourceCharLimit(maxChars, ceiling, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Enabled_RejectsInvalidOutputCeilings(int ceiling)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TranslationOutputBudget.Compute(100, 1, 1, ceiling, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => TranslationOutputBudget.GetSourceCharLimit(100, ceiling, true));
    }
}
