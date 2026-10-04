using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public class TranslationGenerationBudgetTests
{
    [Fact]
    public void RecoveryBudget_IsPerOriginalRowAndDoesNotResetOnReregistration()
    {
        var budget = new TranslationGenerationBudget(1);
        budget.RegisterRow(1, 1000, 0);
        budget.RegisterRow(2, 1000, 0);
        budget.Consume(new long[] { 1, 1 }, recovery: true);
        budget.RegisterRow(1, 1000, 0); // Model/key change must not restore allowance.
        var ex = Assert.Throws<TranslationGenerationLimitException>(() => budget.Consume(new long[] { 1 }, true));
        Assert.False(ex.IsRunLimit);
        budget.Consume(new long[] { 2 }, true);
        Assert.Equal(2, budget.TotalCalls);
    }

    [Fact]
    public async Task ConcurrentCallLimit_IsAtomicAndNeverOvershoots()
    {
        var budget = new TranslationGenerationBudget(8, 5);
        var accepted = 0;
        await Task.WhenAll(Enumerable.Range(1, 50).Select(_ => Task.Run(() =>
        {
            try { budget.Consume(Array.Empty<long>(), false); Interlocked.Increment(ref accepted); }
            catch (TranslationGenerationLimitException ex) { Assert.True(ex.IsRunLimit); }
        })));
        Assert.Equal(5, accepted);
        Assert.Equal(5, budget.TotalCalls);
    }

    // A batch mixing a row sent before with a new row is a retry only for the first.
    [Fact]
    public void RecoveryCalls_AreCountedOnlyForTheRetriedRows()
    {
        var budget = new TranslationGenerationBudget(1, 100);
        budget.Consume(new long[] { 1 }, recovery: false);
        Assert.Equal(new long[] { 1 }, budget.GetAttempted(new long[] { 1, 2 }));

        budget.Consume(new long[] { 1, 2 }, retriedRowIds: new long[] { 1 });

        Assert.Equal(new long[] { 1 }, budget.GetRowsOutOfRecoveryCalls(new long[] { 1, 2 }));
        Assert.Throws<TranslationGenerationLimitException>(() => budget.Consume(new long[] { 1, 2 }, retriedRowIds: new long[] { 1 }));
        budget.Consume(new long[] { 2 }, recovery: true);
        Assert.Equal(3, budget.TotalCalls);
    }

    [Fact]
    public void MixedBatch_RejectionDoesNotConsumeOtherRowsOrTotal()
    {
        var budget = new TranslationGenerationBudget(1, 100);
        budget.Consume(new long[] { 1 }, true);
        Assert.Throws<TranslationGenerationLimitException>(() => budget.Consume(new long[] { 2, 1 }, true));
        budget.Consume(new long[] { 2 }, true);
        Assert.Equal(2, budget.TotalCalls);
    }
}
