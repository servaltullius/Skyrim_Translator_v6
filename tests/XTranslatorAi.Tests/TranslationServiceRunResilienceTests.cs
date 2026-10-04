using System.Text.Json;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>How a run keeps going, or stops cleanly, when single rows or calls fail.</summary>
public sealed class TranslationServiceRunResilienceTests
{
    // After an API-key switch the shared budget remembers rows tried under the old key. One of them had used its
    // whole retry allowance; the batch it shared with a row never sent failed as a whole and both became Error.
    [Fact]
    public async Task RowOutOfRetries_IsMarkedErrorAndItsBatchPeerIsStillTranslated()
    {
        await using var fixture = await Fixture.CreateAsync(("Exhausted row", "MESG:DESC"), ("Fresh row", "MESG:DESC"));
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
        await using var fixture = await Fixture.CreateAsync(
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

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path;
        public ProjectDb Db { get; }
        public FakeClient Client { get; } = new();
        public TranslationService Service { get; }
        public IReadOnlyList<long> Ids { get; }

        public TranslateIdsRequest Request => new(
            "DUMMY", "gemini-2.5-flash", "english", "english", "base", Ids,
            BatchSize: 1, MaxChars: 5000, MaxConcurrency: 1, Temperature: 0,
            MaxOutputTokens: 1024, MaxRetries: 0, UseRecStyleHints: false,
            EnableRepairPass: false, EnableSessionTermMemory: false,
            OnRowUpdated: null, WaitIfPaused: null, CancellationToken: CancellationToken.None,
            EnablePromptCache: false, EnableRiskyCandidateRerank: false);

        private Fixture(string path, ProjectDb db, IReadOnlyList<long> ids)
        {
            _path = path;
            Db = db;
            Ids = ids;
            Service = new TranslationService(db, Client);
        }

        public static async Task<Fixture> CreateAsync(params (string Source, string Rec)[] rows)
        {
            var path = Path.Combine(Path.GetTempPath(), $"xt-resilience-{Guid.NewGuid():N}.sqlite");
            var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            await db.UpsertProjectAsync(new ProjectInfo(1, "dummy.xml", "Dummy", null, "english", "english",
                "1", false, "<?xml version=\"1.0\"?>", "gemini-2.5-flash", "base", null, false, now, now), CancellationToken.None);
            await db.BulkInsertStringsAsync(rows.Select((row, index) => (
                OrderIndex: index, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null,
                Edid: (string?)$"Row{index:00}", Rec: (string?)row.Rec, SourceText: row.Source, DestText: "",
                Status: StringEntryStatus.Pending, RawStringXml: "<r/>"
            )).ToArray(), CancellationToken.None);
            var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
            return new Fixture(path, db, ids.OrderBy(id => id).ToArray());
        }

        public async Task<Dictionary<long, StringEntry>> RowsAsync()
            => (await Db.GetStringsAsync(100, 0, CancellationToken.None)).ToDictionary(row => row.Id);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            TestDbHelper.TryDeleteDbFiles(_path);
        }
    }

    /// <summary>Echoes the source back: as text for text-only prompts, as the translations JSON for batch prompts.</summary>
    internal sealed class FakeClient : IGeminiClient
    {
        private readonly object _gate = new();
        public int Calls { get; private set; }
        public int CacheCreates { get; private set; }
        public List<string> DeletedCaches { get; } = new();
        public List<GeminiGenerateContentRequest> Requests { get; } = new();
        public Func<int, GeminiGenerateContentRequest, string?>? ResponseOverride { get; set; }
        public Func<int, string>? CreateCache { get; set; }

        public Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        {
            int call;
            lock (_gate)
            {
                call = ++Calls;
                Requests.Add(request);
            }

            if (ResponseOverride?.Invoke(call, request) is { } response) return Task.FromResult(response);
            var prompt = request.Contents[0].Parts[0].Text!;
            const string jsonMarker = "Input JSON:";
            var json = prompt.IndexOf(jsonMarker, StringComparison.Ordinal);
            if (json >= 0)
            {
                using var payload = JsonDocument.Parse(prompt[(json + jsonMarker.Length)..]);
                var translations = payload.RootElement.GetProperty("items").EnumerateArray()
                    .Select(item => new { id = item.GetProperty("id").GetInt64(), text = Strip(item.GetProperty("text").GetString()!) })
                    .ToArray();
                return Task.FromResult(JsonSerializer.Serialize(new { translations }));
            }

            const string start = "<<<TEXT";
            const string end = "TEXT>>>";
            var content = prompt[(prompt.IndexOf(start, StringComparison.Ordinal) + start.Length)..];
            content = content.TrimStart('\r', '\n');
            content = content[..content.LastIndexOf(end, StringComparison.Ordinal)].TrimEnd('\r', '\n');
            return Task.FromResult(Strip(content));
        }

        private static string Strip(string text) => GlossarySemanticHintInjector.Strip(PlaceholderSemanticHintInjector.Strip(text));

        public async Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
            => new[] { await GenerateContentAsync(apiKey, modelName, request, cancellationToken) };

        public Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
            => Task.FromResult(Math.Max(1, text.Length / 4));

        public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText, TimeSpan ttl, CancellationToken cancellationToken)
        {
            int attempt;
            lock (_gate) attempt = ++CacheCreates;
            return Task.FromResult(CreateCache?.Invoke(attempt) ?? $"cachedContents/fixture-{attempt}");
        }

        public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken)
        {
            lock (_gate) DeletedCaches.Add(cacheName);
            return Task.CompletedTask;
        }
    }
}
