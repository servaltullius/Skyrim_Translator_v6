using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>How a run keeps going when single rows or calls fail.</summary>
public sealed class TranslationServiceRunResilienceTests
{
    // After an API-key switch the shared budget remembers rows tried under the old key. One of them had used its
    // whole retry allowance; the batch it shared with a row never sent failed as a whole and both became Error.
    [Fact]
    public async Task RowOutOfRetries_IsMarkedErrorAndItsBatchPeerIsStillTranslated()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Exhausted row", "MESG:DESC"), ("Fresh row", "MESG:DESC"));
        var (exhausted, fresh) = (fixture.Ids[0], fixture.Ids[1]);
        var budget = new TranslationGenerationBudget(maxRecoveryCallsPerRow: 1, maxTotalCalls: 100);
        budget.Consume(new[] { exhausted }, recovery: true);

        await fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 2, GenerationBudget = budget });

        var rows = await fixture.RowsAsync();
        Assert.Equal(StringEntryStatus.Error, rows[exhausted].Status);
        Assert.Contains("추가 생성 호출 상한", rows[exhausted].ErrorMessage);
        Assert.Equal(StringEntryStatus.Done, rows[fresh].Status);
        Assert.Equal("Fresh row", rows[fresh].DestText);
        Assert.DoesNotContain(fixture.Client.Requests, request => request.Contents[0].Parts[0].Text!.Contains("Exhausted row"));
    }

    // A row sent before, reaching its limit while the batch is split after a bad response, used to stop every
    // split level, so the right half was never sent and all four rows became Error.
    [Fact]
    public async Task RowReachingItsRetryLimitDuringSplitFallback_DoesNotFailItsPeers()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("Retried row", "MESG:DESC"), ("Second row", "MESG:DESC"), ("Third row", "MESG:DESC"), ("Fourth row", "MESG:DESC"));
        var retried = fixture.Ids[0];
        var budget = new TranslationGenerationBudget(maxRecoveryCallsPerRow: 1, maxTotalCalls: 100);
        budget.Consume(new[] { retried }, recovery: false);
        fixture.Client.ResponseOverride = (call, _) => call == 1 ? "invalid batch JSON" : null;

        await fixture.Service.TranslateIdsAsync(fixture.Request with { BatchSize = 4, GenerationBudget = budget });

        var rows = await fixture.RowsAsync();
        Assert.Equal(StringEntryStatus.Error, rows[retried].Status);
        Assert.Contains("추가 생성 호출 상한", rows[retried].ErrorMessage);
        foreach (var id in fixture.Ids.Skip(1))
        {
            Assert.Equal(StringEntryStatus.Done, rows[id].Status);
            Assert.Equal(rows[id].SourceText, rows[id].DestText);
        }
    }
}
