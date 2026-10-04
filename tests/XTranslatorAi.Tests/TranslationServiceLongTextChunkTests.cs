using System.Collections.Concurrent;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// A book longer than the request limit is split into chunks, translated (two at a time when the run allows five
/// requests) and joined again. These pin down the join itself: order, separators, failures and cancellation.
/// </summary>
public sealed class TranslationServiceLongTextChunkTests
{
    private const int Paragraphs = 8;

    [Fact]
    public async Task ParallelChunks_AreJoinedInSourceOrderWhenTheFirstChunkAnswersLast()
    {
        var source = BuildBook("\n\n");
        await using var fixture = await TranslationRunFixture.CreateAsync((source, "BOOK:DESC"));
        var lastChunkAnswered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new ConcurrentQueue<string>();
        fixture.Client.BeforeGenerate = async (_, request, ct) =>
        {
            var chunk = ChunkOf(request);
            if (chunk.Contains(Marker(1), StringComparison.Ordinal))
            {
                // Hold the first chunk until the last one has been answered by the other parallel slot.
                await lastChunkAnswered.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            }

            answered.Enqueue(chunk);
            if (chunk.Contains(Marker(Paragraphs), StringComparison.Ordinal))
            {
                lastChunkAnswered.TrySetResult();
            }
        };
        fixture.Client.ResponseOverride = (_, request) => Translate(ChunkOf(request));

        await fixture.Service.TranslateIdsAsync(ParallelRequest(fixture));

        var row = Assert.Single((await fixture.RowsAsync()).Values);
        Assert.True(row.Status == StringEntryStatus.Done, row.ErrorMessage);
        Assert.Equal(Translate(source), row.DestText);
        Assert.True(answered.Count >= 3, $"expected several chunks, got {answered.Count}");
        Assert.Contains(Marker(1), answered.Last(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n\n", 5)]
    [InlineData("\r\n\r\n", 5)]
    [InlineData("\n\n\n", 5)]
    [InlineData(" \n\n  ", 5)]
    [InlineData("[pagebreak]\n", 5)]
    [InlineData("\n\n", 1)]
    [InlineData("\r\n\r\n", 1)]
    [InlineData("[pagebreak]\n", 1)]
    public async Task Chunks_KeepEverySeparatorExactlyOnce(string separator, int maxConcurrency)
    {
        var source = BuildBook(separator);
        await using var fixture = await TranslationRunFixture.CreateAsync((source, "BOOK:DESC"));
        fixture.Client.ResponseOverride = (_, request) => Translate(ChunkOf(request));

        await fixture.Service.TranslateIdsAsync(ParallelRequest(fixture) with { MaxConcurrency = maxConcurrency });

        var row = Assert.Single((await fixture.RowsAsync()).Values);
        Assert.True(row.Status == StringEntryStatus.Done, row.ErrorMessage);
        Assert.Equal(Translate(source), row.DestText);
        Assert.True(fixture.Client.Calls >= 3, $"expected several chunks, got {fixture.Client.Calls} calls");
        // Every paragraph was sent exactly once: no chunk overlaps another or is left out.
        for (var i = 1; i <= Paragraphs; i++)
        {
            Assert.Single(fixture.Client.Requests, r => ChunkOf(r).Contains(Marker(i), StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("invalid-output", 5)]
    [InlineData("http-error", 5)]
    [InlineData("invalid-output", 1)]
    [InlineData("http-error", 1)]
    public async Task FailingChunk_LeavesTheRowInErrorWithoutAPartialTranslation(string failure, int maxConcurrency)
    {
        var source = BuildBook("\n\n");
        await using var fixture = await TranslationRunFixture.CreateAsync((source, "BOOK:DESC"));
        var failing = Marker(5);
        fixture.Client.BeforeGenerate = (_, request, _) =>
            failure == "http-error" && ChunkOf(request).Contains(failing, StringComparison.Ordinal)
                ? Task.FromException(new GeminiHttpException("generateContent", 500, "fixture", null, "fixture"))
                : Task.CompletedTask;
        fixture.Client.ResponseOverride = (_, request) =>
        {
            var chunk = ChunkOf(request);
            return failure == "invalid-output" && chunk.Contains(failing, StringComparison.Ordinal)
                ? ""
                : Translate(chunk);
        };

        await fixture.Service.TranslateIdsAsync(ParallelRequest(fixture) with { MaxConcurrency = maxConcurrency });

        var row = Assert.Single((await fixture.RowsAsync()).Values);
        Assert.Equal(StringEntryStatus.Error, row.Status);
        Assert.False(string.IsNullOrWhiteSpace(row.ErrorMessage));
        // The chunks that did translate are not saved on their own.
        Assert.Equal("", row.DestText);
        Assert.Contains(fixture.Client.Requests, r => ChunkOf(r).Contains(Marker(1), StringComparison.Ordinal));
        // A bad answer is retried in smaller pieces down to the minimum chunk; a transport error is not.
        var failingRequests = fixture.Client.Requests.Count(r => ChunkOf(r).Contains(failing, StringComparison.Ordinal));
        if (failure == "http-error") Assert.Equal(1, failingRequests);
        else Assert.True(failingRequests > 1, $"expected smaller retries, got {failingRequests}");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(1)]
    public async Task CancellationDuringChunks_StopsTheRunAndPutsTheRowBackToPending(int maxConcurrency)
    {
        var source = BuildBook("\n\n");
        await using var fixture = await TranslationRunFixture.CreateAsync((source, "BOOK:DESC"));
        using var cts = new CancellationTokenSource();
        var cancelAt = maxConcurrency > 1 ? 2 : 1;
        fixture.Client.BeforeGenerate = async (call, _, ct) =>
        {
            if (call == cancelAt)
            {
                cts.Cancel();
            }
            // Requests already in flight honour the token, like the real client.
            if (call < cancelAt)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            ct.ThrowIfCancellationRequested();
        };
        fixture.Client.ResponseOverride = (_, request) => Translate(ChunkOf(request));

        var run = fixture.Service.TranslateIdsAsync(ParallelRequest(fixture) with
        {
            MaxConcurrency = maxConcurrency,
            CancellationToken = cts.Token,
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(cancelAt, fixture.Client.Calls);
        var row = Assert.Single((await fixture.RowsAsync()).Values);
        Assert.Equal(StringEntryStatus.Pending, row.Status);
        Assert.Equal("", row.DestText);
    }

    private static TranslateIdsRequest ParallelRequest(TranslationRunFixture fixture)
        // Five allowed requests turn on two parallel chunks (TranslateIdsCoreBodyAsync).
        => fixture.Request with { MaxChars = 1000, MaxConcurrency = 5 };

    private static string Marker(int paragraph) => $"Paragraph {paragraph:00}";

    /// <summary>Eight ~400-character paragraphs, so a 1000-character limit gives several chunks.</summary>
    private static string BuildBook(string separator)
        => string.Join(separator, Enumerable.Range(1, Paragraphs).Select(i =>
            $"{Marker(i)} begins here. " + string.Concat(Enumerable.Repeat("The road winds on through the quiet hills. ", 9)).TrimEnd()));

    /// <summary>A visible change, so the saved text must have come from the model's answers.</summary>
    private static string Translate(string text) => text.Replace("Paragraph", "Absatz", StringComparison.Ordinal);

    private static string ChunkOf(GeminiGenerateContentRequest request)
        => GlossarySemanticHintInjector.Strip(PlaceholderSemanticHintInjector.Strip(
            EchoGeminiClient.GetTextOnlySource(request.Contents[0].Parts[0].Text!)));
}
