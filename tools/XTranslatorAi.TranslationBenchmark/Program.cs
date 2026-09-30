using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

// The baseline Korean translations are deliberately not an input to this program.
const string model = "gemini-3.8-flash";
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
if (args.Length == 3 && args[0] == "evaluate-prompts")
    return await PromptEvaluationCommand.RunAsync(args[1], args[2]);
if (args.Length != 3 || args[0] is not ("prepare" or "estimate" or "translate" or "export"))
{
    Console.Error.WriteLine("Usage: TranslationBenchmark prepare|estimate|translate|export original.esp NEW-EXPERIMENT-FOLDER");
    Console.Error.WriteLine("translate reads GEMINI_API_KEY in memory; never pass a key as a command-line argument.");
    return 2;
}

var mode = args[0];
var input = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(args[2]);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var ct = cancellation.Token;
Directory.CreateDirectory(root);
var manifestPath = Path.Combine(root, "experiment.json");
var dbPath = Path.Combine(root, "translation.stproj.sqlite");
var startedAt = DateTimeOffset.UtcNow;
var elapsed = Stopwatch.StartNew();
var logger = new UsageLogger(Path.Combine(root, "usage.jsonl"));

try
{
    var document = await PluginReader.ReadAsync(input, new(SourceEncoding: "utf-8"), ct);
    if (document.Info.Diagnostics.Any(d => d.BlocksExport))
        throw new InvalidDataException("Source has blocking plugin diagnostics.");
    using var promptStream = typeof(SystemPromptBuilder).Assembly.GetManifestResourceStream("SkyrimPrompt")!;
    using var promptReader = new StreamReader(promptStream, Encoding.UTF8);
    var basePrompt = await promptReader.ReadToEndAsync(ct);
    var prompt = new SystemPromptBuilder().Build(basePrompt, false, null, false, null);
    var promptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
    var hasExperiment = File.Exists(manifestPath);
    if (!hasExperiment && File.Exists(dbPath))
        throw new InvalidDataException("Refusing an existing database without experiment provenance.");
    Experiment experiment;
    if (hasExperiment)
    {
        experiment = JsonSerializer.Deserialize<Experiment>(await File.ReadAllTextAsync(manifestPath, ct))!;
        if (experiment.Model != model || experiment.SourceSha256 != document.Info.Sha256 || experiment.PromptSha256 != promptHash)
            throw new InvalidDataException("Model, source or prompt changed. Use a new experiment folder.");
    }
    else
    {
        if (mode == "export") throw new InvalidOperationException("Prepare a fresh experiment first.");
        experiment = new(model, "low", document.Info.Sha256, promptHash, startedAt, 0, 0, false, false, false);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(experiment, jsonOptions), ct);
        await File.WriteAllTextAsync(Path.Combine(root, "system-prompt.txt"), prompt, ct);
        await File.WriteAllTextAsync(Path.Combine(root, "source.json"), JsonSerializer.Serialize(new { document.Info, document.Fields }, jsonOptions), ct);
    }

    await using var db = await ProjectDb.OpenOrCreateAsync(dbPath, ct);
    var source = await db.TryGetPluginSourceAsync(ct);
    if (source == null)
    {
        if (hasExperiment && await db.GetStringCountAsync(ct) != 0)
            throw new InvalidDataException("Existing database is not a plugin experiment.");
        var glossaryCount = (await db.GetGlossaryAsync(ct)).Count;
        var tmCount = (await db.GetTranslationMemoryAsync("english", "korean", ct)).Count;
        if (glossaryCount != 0 || tmCount != 0)
            throw new InvalidDataException("A fresh experiment requires empty glossary and translation memory.");
        var project = new ProjectInfo(1, input, Path.GetFileName(input), BethesdaFranchise.ElderScrolls,
            "english", "korean", "", false, "", model, basePrompt, null, false, startedAt, startedAt);
        await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project, "utf-8", ct);
    }
    else if (source.Info.Sha256 != document.Info.Sha256)
        throw new InvalidDataException("Database belongs to a different source plugin.");
    await db.ResetInProgressToPendingAsync(ct);
    var rows = await ReadRowsAsync(db, ct);
    if (rows.Count != document.Fields.Count) throw new InvalidDataException("Source/database field count differs.");
    if (!hasExperiment && rows.Any(r => r.Status != StringEntryStatus.Pending || r.DestText != r.SourceText))
        throw new InvalidDataException("Fresh database already contains destination translations.");

    Console.WriteLine(JsonSerializer.Serialize(new {
        Mode = mode, Model = model, Thinking = "low", Fields = rows.Count,
        SourceCharacters = rows.Sum(r => r.SourceText.Length),
        SourceRowsWithHangul = rows.Count(r => Regex.IsMatch(r.SourceText, "[가-힣]")),
        Pending = rows.Count(r => r.Status is StringEntryStatus.Pending or StringEntryStatus.Error),
        experiment.InitialGlossaryCount, experiment.InitialTmCount,
        ExistingKoreanBaselineImported = false,
        GlobalGlossary = false, GlobalTm = false, ModelOverride = false,
        PromptCache = false, ProjectContext = false, BatchApi = false, Flex = false
    }));

    if (mode is "translate" or "estimate")
    {
        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("GEMINI_API_KEY is missing.");
        using var handler = new SafeApiHandler(Path.Combine(root, "requests.jsonl"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        var client = new GeminiClient(http, logger);
        var models = await client.ListModelsAsync(key, ct);
        if (!models.Any(m => m.Name == "models/" + model && m.SupportedGenerationMethods?.Contains("generateContent") == true))
            throw new InvalidOperationException("The key cannot access the exact requested model; no substitute will be used.");
        if (mode == "estimate")
        {
            // Token counting only: never generate a paid sample or export a plugin.
            var estimate = await new TranslationCostEstimator(db, client).EstimateAsync(new(
                ApiKey: key, ModelName: model, SourceLang: "english", TargetLang: "korean",
                SystemPrompt: prompt, BatchSize: 12, MaxChars: 15000, MaxOutputTokens: 65536,
                RunSampleToEstimateOutputTokens: false, IncludeCompletedItems: false), ct);
            var report = new {
                Estimate = estimate, InvocationUsage = logger.Summary(),
                Note = "Input tokens counted without translation generation. Output is a heuristic, not a spending cap; thinking, session context, repairs and retries can add cost. Use the standard price without prompt caching. Free Tier eligibility is not determined by this check."
            };
            await File.WriteAllTextAsync(Path.Combine(root, "cost-estimate.json"),
                JsonSerializer.Serialize(report, jsonOptions), ct);
            Console.WriteLine(JsonSerializer.Serialize(report));
            return 0;
        }
        var ids = rows.Where(r => r.Status is StringEntryStatus.Pending or StringEntryStatus.Error).Select(r => r.Id).ToArray();
        var completedIds = rows.Where(r => r.Status is StringEntryStatus.Done or StringEntryStatus.Edited).Select(r => r.Id).ToHashSet();
        var announced = completedIds.Count;
        var progressGate = new object();
        try
        {
            await new TranslationService(db, client).TranslateIdsAsync(new(
                ApiKey: key, ModelName: model, SourceLang: "english", TargetLang: "korean", SystemPrompt: prompt,
                Ids: ids, BatchSize: 12, MaxChars: 15000, MaxConcurrency: 2, Temperature: 0.1,
                MaxOutputTokens: 65536, MaxRetries: 3, UseRecStyleHints: true, EnableRepairPass: true,
                EnableSessionTermMemory: true,
                OnRowUpdated: (id, status, _) => {
                    lock (progressGate) {
                        if (status == StringEntryStatus.Done) completedIds.Add(id);
                        if (completedIds.Count - announced >= 50) {
                            announced = completedIds.Count;
                            Console.WriteLine($"Progress: {completedIds.Count}/{rows.Count}; elapsed {elapsed.Elapsed.TotalSeconds:F0}s");
                        }
                    }
                    return Task.CompletedTask;
                },
                WaitIfPaused: null, CancellationToken: ct, SemanticRepairMode: PlaceholderSemanticRepairMode.Soft,
                KeepSkyrimTagsRaw: true, EnableDialogueContextWindow: true, EnablePromptCache: false,
                EnableQualityEscalation: false, EnableRiskyCandidateRerank: true, RiskyCandidateCount: 3,
                ThinkingConfigOverride: new(null, "low"), EnableAdaptiveOutputBudget: false, EnableBookContext: false));
        }
        finally
        {
            // Includes cancellation/quota failures: completed results remain inspectable and resumable.
            rows = await ReadRowsAsync(db, CancellationToken.None);
            await SaveResultsAsync(rows, document, root, startedAt, elapsed.Elapsed, logger, jsonOptions);
        }
    }
    else await SaveResultsAsync(rows, document, root, startedAt, elapsed.Elapsed, logger, jsonOptions);

    if (mode != "prepare")
    {
        var incomplete = rows.Count(r => r.Status is not (StringEntryStatus.Done or StringEntryStatus.Edited));
        if (incomplete != 0) {
            Console.WriteLine($"INCOMPLETE: {incomplete} rows unfinished; database saved; no ESP exported.");
            return 1;
        }
        var edits = await db.GetPluginTranslationsForExportAsync(document.Info.Sha256, ct);
        var export = await PluginWriter.ExportAsync(document, edits,
            new(Path.Combine(root, "translated-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")), "utf-8"), ct);
        await File.WriteAllTextAsync(Path.Combine(root, "export.json"), JsonSerializer.Serialize(export, jsonOptions), ct);
        Console.WriteLine(JsonSerializer.Serialize(export));
    }
    return 0;
}
catch (Exception ex)
{
    // Never print HTTP exception messages, request URLs, masks, or credentials.
    Console.Error.WriteLine(JsonSerializer.Serialize(new {
        Error = ex is GeminiHttpException ? "GeminiHttpFailure" : ex.GetType().Name,
        StatusCode = ex is GeminiHttpException he ? (int?)he.StatusCode : null,
        Operation = ex is GeminiHttpException op ? op.Operation : null,
        Detail = ex is GeminiException or HttpRequestException ? "API request failed; credentials omitted." : ex.Message
    }));
    return 1;
}

static async Task<List<StringEntry>> ReadRowsAsync(ProjectDb db, CancellationToken ct)
{
    var rows = new List<StringEntry>();
    var count = await db.GetStringCountAsync(ct);
    for (var offset = 0; offset < count; offset += 500)
        rows.AddRange(await db.GetStringsAsync(500, offset, ct));
    return rows;
}

static async Task SaveResultsAsync(List<StringEntry> rows, PluginDocument document, string root,
    DateTimeOffset started, TimeSpan duration, UsageLogger logger, JsonSerializerOptions options)
{
    var fields = document.Fields.ToDictionary(f => f.OrderIndex);
    await File.WriteAllTextAsync(Path.Combine(root, "rows.json"), JsonSerializer.Serialize(rows.Select(r => new {
        fields[r.OrderIndex].Key, r.Id, r.OrderIndex, r.Rec, r.Edid, r.SourceText, r.DestText,
        Status = r.Status.ToString(), HasError = !string.IsNullOrEmpty(r.ErrorMessage)
    }), options));
    await File.WriteAllTextAsync(Path.Combine(root, "run.json"), JsonSerializer.Serialize(new {
        Model = "gemini-3.8-flash", Thinking = "low", StartedAt = started, Seconds = duration.TotalSeconds,
        Fields = rows.Count, StatusCounts = rows.GroupBy(r => r.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        Complete = rows.All(r => r.Status is StringEntryStatus.Done or StringEntryStatus.Edited),
        InvocationUsage = logger.Summary(), UsageFile = "usage.jsonl",
        Note = "Usage is per invocation; usage.jsonl retains all calls across resumes. Cost is a token-price estimate, not a billing receipt."
    }, options));
}

sealed record Experiment(string Model, string Thinking, string SourceSha256, string PromptSha256,
    DateTimeOffset CreatedAt, int InitialGlossaryCount, int InitialTmCount,
    bool ImportedKoreanBaseline, bool GlobalGlossary, bool GlobalTm);

sealed class UsageLogger(string path) : IGeminiCallLogger
{
    private readonly object _gate = new();
    private readonly List<GeminiCallLogEntry> _entries = [];
    public void Log(GeminiCallLogEntry entry)
    {
        lock (_gate) {
            _entries.Add(entry);
            File.AppendAllText(path, JsonSerializer.Serialize(new {
                entry.StartedAt, Seconds = entry.Duration.TotalSeconds, Operation = entry.Operation.ToString(),
                entry.ModelName, entry.StatusCode, entry.Success, entry.PromptTokens, entry.CompletionTokens,
                entry.OutputTokens, entry.ThoughtsTokens, entry.TotalTokens, entry.CachedContentTokens,
                entry.CostUsd, entry.Purpose, entry.FinishReason
            }) + "\n");
        }
    }
    public object Summary() {
        lock (_gate) {
            var generations = _entries.Where(e => e.Operation == GeminiCallOperation.GenerateContent).ToArray();
            return new { GenerationCalls = generations.Length, FailedCalls = generations.Count(e => !e.Success),
                PromptTokens = generations.Sum(e => (long)(e.PromptTokens ?? 0)),
                CompletionTokensIncludingThinking = generations.Sum(e => (long)(e.CompletionTokens ?? 0)),
                EstimatedUsd = generations.Sum(e => e.CostUsd ?? 0),
                CallsWithoutUsage = generations.Count(e => e.PromptTokens == null || e.CompletionTokens == null) };
        }
    }
}

sealed class SafeApiHandler(string tracePath) : DelegatingHandler(new HttpClientHandler())
{
    private readonly object _gate = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        if (uri.Scheme != "https" || uri.Host != "generativelanguage.googleapis.com")
            throw new InvalidOperationException("Unexpected API destination.");
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var keyParameter = query.SingleOrDefault(p => p.StartsWith("key=", StringComparison.Ordinal));
        if (keyParameter != null) {
            request.Headers.Add("x-goog-api-key", Uri.UnescapeDataString(keyParameter[4..]));
            request.RequestUri = new UriBuilder(uri) { Query = string.Join("&", query.Where(p => p != keyParameter)) }.Uri;
        }
        if (uri.AbsolutePath.Contains(":generateContent") && !uri.AbsolutePath.EndsWith("/gemini-3.8-flash:generateContent"))
            throw new InvalidOperationException("A different model was requested.");
        // Only the model input body is retained, never HTTP headers or credential-bearing URLs.
        if (request.Content != null) {
            var body = await request.Content.ReadAsStringAsync(ct);
            using var parsed = JsonDocument.Parse(body);
            lock (_gate) File.AppendAllText(tracePath, JsonSerializer.Serialize(new {
                At = DateTimeOffset.UtcNow, Path = uri.AbsolutePath, Body = parsed.RootElement
            }) + "\n");
        }
        return await base.SendAsync(request, ct);
    }
}
