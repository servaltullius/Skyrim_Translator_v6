using System.Text.Json;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests.TestSupport;

/// <summary>A project DB with pending rows and a TranslationService over <see cref="EchoGeminiClient"/>.</summary>
internal sealed class TranslationRunFixture : IAsyncDisposable
{
    private readonly string _path;
    public ProjectDb Db { get; }
    public EchoGeminiClient Client { get; } = new();
    public TranslationService Service { get; }
    public IReadOnlyList<long> Ids { get; }

    public TranslateIdsRequest Request => new(
        "DUMMY", "gemini-2.5-flash", "english", "english", "base", Ids,
        BatchSize: 1, MaxChars: 5000, MaxConcurrency: 1, Temperature: 0,
        MaxOutputTokens: 1024, MaxRetries: 0, UseRecStyleHints: false,
        EnableRepairPass: false, EnableSessionTermMemory: false,
        OnRowUpdated: null, WaitIfPaused: null, CancellationToken: CancellationToken.None,
        EnablePromptCache: false, EnableRiskyCandidateRerank: false);

    private TranslationRunFixture(string path, ProjectDb db, IReadOnlyList<long> ids)
    {
        _path = path;
        Db = db;
        Ids = ids;
        Service = new TranslationService(db, Client);
    }

    public static async Task<TranslationRunFixture> CreateAsync(params (string Source, string Rec)[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-run-{Guid.NewGuid():N}.sqlite");
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
        return new TranslationRunFixture(path, db, ids.OrderBy(id => id).ToArray());
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
internal sealed class EchoGeminiClient : IGeminiClient
{
    private readonly object _gate = new();
    public int Calls { get; private set; }
    public int CacheCreates { get; private set; }
    public List<string> DeletedCaches { get; } = new();
    public List<GeminiGenerateContentRequest> Requests { get; } = new();
    public Func<int, GeminiGenerateContentRequest, string?>? ResponseOverride { get; set; }
    /// <summary>Runs before the n-th generation is answered (from 1); it may delay, throw or observe cancellation.</summary>
    public Func<int, GeminiGenerateContentRequest, CancellationToken, Task>? BeforeGenerate { get; set; }
    /// <summary>Answers the n-th cache creation (from 1); by default a new name each time.</summary>
    public Func<int, Task<string>>? CreateCache { get; set; }

    public async Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
    {
        int call;
        lock (_gate)
        {
            call = ++Calls;
            Requests.Add(request);
        }

        if (BeforeGenerate != null) await BeforeGenerate(call, request, cancellationToken);
        if (ResponseOverride?.Invoke(call, request) is { } response) return response;
        var prompt = request.Contents[0].Parts[0].Text!;
        const string jsonMarker = "Input JSON:";
        var json = prompt.IndexOf(jsonMarker, StringComparison.Ordinal);
        if (json >= 0)
        {
            using var payload = JsonDocument.Parse(prompt[(json + jsonMarker.Length)..]);
            var translations = payload.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => new { id = item.GetProperty("id").GetInt64(), text = Strip(item.GetProperty("text").GetString()!) })
                .ToArray();
            return JsonSerializer.Serialize(new { translations });
        }

        return Strip(GetTextOnlySource(prompt));
    }

    /// <summary>The text between the text-only prompt's markers, as the model sees it (with the end sentinel).</summary>
    public static string GetTextOnlySource(string prompt)
    {
        const string start = "<<<TEXT";
        const string end = "TEXT>>>";
        var content = prompt[(prompt.IndexOf(start, StringComparison.Ordinal) + start.Length)..];
        content = content.TrimStart('\r', '\n');
        return content[..content.LastIndexOf(end, StringComparison.Ordinal)].TrimEnd('\r', '\n');
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
        return CreateCache?.Invoke(attempt) ?? Task.FromResult($"cachedContents/fixture-{attempt}");
    }

    public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken)
    {
        lock (_gate) DeletedCaches.Add(cacheName);
        return Task.CompletedTask;
    }
}
