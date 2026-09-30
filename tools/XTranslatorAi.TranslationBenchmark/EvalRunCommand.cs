using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

// Runs a frozen eval-v2 dataset through the production TranslationService with
// the app's default options. Every variant uses the same rows, prompt and
// built-in glossary; only the model or one option differs. Spending is capped
// across all runs under the evaluation root.
internal static class EvalRunCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, (string Model, bool BookContext, bool BooksOnly)> Variants = new()
    {
        ["baseline"] = (GeminiModelCatalog.DefaultModel, false, false),
        ["lite"] = (GeminiModelCatalog.LowCostModel, false, false),
        ["bookctx"] = (GeminiModelCatalog.DefaultModel, true, true),
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 3 || !Variants.TryGetValue(args[1], out var variant))
        {
            Console.Error.WriteLine("Usage: eval-run DATASET.json baseline|lite|bookctx RUN-FOLDER --budget-usd N [--every K] [--estimate-only] [--resume]");
            return 2;
        }
        var datasetPath = Path.GetFullPath(args[0]);
        var root = Path.GetFullPath(args[2]);
        var budget = ReadOption(args, "--budget-usd") is { } b ? double.Parse(b, System.Globalization.CultureInfo.InvariantCulture)
            : throw new ArgumentException("--budget-usd is required.");
        var every = ReadOption(args, "--every") is { } e ? int.Parse(e) : 1;
        // Token counting only (countTokens is not billed); no translation is generated.
        var estimateOnly = args.Contains("--estimate-only");
        // --resume continues an interrupted run: only never-attempted rows are sent, so rows that
        // already failed keep their result and variants stay comparable.
        var resume = args.Contains("--resume");
        var hasContent = Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any();
        if (hasContent && !resume) throw new InvalidDataException("Use a new, empty run folder, or --resume.");
        if (resume && !File.Exists(Path.Combine(root, "provenance.json")))
            throw new InvalidDataException("Nothing to resume in this folder.");

        // All runs of one evaluation live next to each other; their usage counts toward one cap.
        var evalRoot = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(evalRoot);
        var spentBefore = Directory.EnumerateFiles(evalRoot, "usage.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadLines)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Sum(entry => entry.TryGetProperty("CostUsd", out var cost) && cost.ValueKind == JsonValueKind.Number ? cost.GetDouble() : 0);
        if (spentBefore >= budget)
            throw new InvalidOperationException($"Budget exhausted: {spentBefore:F4} of {budget:F2} USD already spent.");

        var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("GEMINI_API_KEY is missing.");

        var datasetBytes = await File.ReadAllBytesAsync(datasetPath);
        var dataset = JsonSerializer.Deserialize<EvalDataset>(datasetBytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing dataset.");
        var rows = dataset.Rows
            .GroupBy(r => r.Group)
            .SelectMany(g => g.Where((_, index) => index % every == 0))
            .Where(r => !variant.BooksOnly || r.Rec == "BOOK:DESC")
            .ToList();

        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        var logger = new BudgetLogger(Path.Combine(root, "usage.jsonl"), budget - spentBefore, cancellation);
        using var promptStream = typeof(SystemPromptBuilder).Assembly.GetManifestResourceStream("SkyrimPrompt")!;
        using var promptReader = new StreamReader(promptStream, Encoding.UTF8);
        var basePrompt = await promptReader.ReadToEndAsync();
        var systemPrompt = new SystemPromptBuilder().Build(basePrompt, false, null, false, null);

        var provenancePath = Path.Combine(root, "provenance.json");
        if (resume)
        {
            using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(provenancePath));
            if (previous.RootElement.GetProperty("Variant").GetString() != args[1]
                || previous.RootElement.GetProperty("DatasetSha256").GetString() != Hash(datasetBytes)
                || previous.RootElement.GetProperty("Every").GetInt32() != every)
                throw new InvalidDataException("Resume must use the same variant, dataset and --every.");
        }
        else await File.WriteAllTextAsync(provenancePath, JsonSerializer.Serialize(new {
            Variant = args[1], variant.Model, Thinking = "app policy", Temperature = 0.1,
            variant.BookContext, Every = every, Rows = rows.Count, BudgetUsd = budget, SpentBeforeUsd = spentBefore,
            DatasetSha256 = Hash(datasetBytes), SystemPromptSha256 = Hash(Encoding.UTF8.GetBytes(systemPrompt)),
            CoreAssemblySha256 = Hash(await File.ReadAllBytesAsync(typeof(TranslationService).Assembly.Location)),
            Options = "App defaults: batch 12 / 15000 chars / parallel 2, REC hints, soft semantic repair, raw Skyrim tags, dialogue context, session term memory, risky rerank x3, built-in TES glossary. Off: prompt cache, TM, project context, quality escalation.",
        }, JsonOptions));

        using var handler = new SafeApiHandler(Path.Combine(root, "requests.jsonl"), variant.Model);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        var client = new GeminiClient(http, logger);
        var models = await client.ListModelsAsync(key, cancellation.Token);
        if (!models.Any(m => m.Name == "models/" + variant.Model && m.SupportedGenerationMethods?.Contains("generateContent") == true))
            throw new InvalidOperationException("The key cannot access the exact requested model; no substitute will be used.");

        await using var globalDb = await ProjectDb.OpenOrCreateAsync(Path.Combine(root, "global.sqlite"), cancellation.Token);
        await new BuiltInGlossaryService().EnsureBuiltInGlossaryAsync(globalDb, cancellation.Token);
        var globalGlossary = await globalDb.GetGlossaryAsync(cancellation.Token);
        var titles = rows.Where(r => r.BookTitle != null && !string.IsNullOrEmpty(r.Edid))
            .GroupBy(r => r.Edid!).ToDictionary(g => g.Key, g => g.First().BookTitle!);

        var results = new List<object>();
        var elapsed = Stopwatch.StartNew();
        var exitCode = 0;
        foreach (var group in rows.GroupBy(r => r.Group))
        {
            var dbPath = Path.Combine(root, group.Key + ".sqlite");
            await using var db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellation.Token);
            var groupRows = group.ToList();
            var fresh = await db.GetStringCountAsync(cancellation.Token) == 0;
            var now = DateTimeOffset.UtcNow;
            if (fresh) await db.UpsertProjectAsync(new ProjectInfo(1, datasetPath, group.Key, BethesdaFranchise.ElderScrolls,
                "english", "korean", "2", false, "", variant.Model, basePrompt, null, false, now, now), cancellation.Token);
            foreach (var term in fresh ? dataset.ProjectGlossaries?.GetValueOrDefault(group.Key) ?? [] : [])
                await db.UpsertGlossaryAsync(new(null, term.Source, term.Target, true, term.Priority,
                    (GlossaryMatchMode)term.MatchMode, (GlossaryForceMode)term.ForceMode, "eval project glossary"), cancellation.Token);
            if (fresh) await db.BulkInsertStringsAsync(groupRows.Select((r, i) =>
                (i, (string?)null, (string?)null, (string?)null, r.Edid, (string?)r.Rec, r.Source, r.Source,
                 StringEntryStatus.Pending, "")), cancellation.Token);
            if (!fresh) await db.ResetInProgressToPendingAsync(cancellation.Token);
            var stored = (await db.GetStringsAsync(groupRows.Count, 0, cancellation.Token))
                .Where(s => s.Status == StringEntryStatus.Pending).ToList();
            if (estimateOnly)
            {
                var estimate = await new TranslationCostEstimator(db, client).EstimateAsync(new(
                    ApiKey: key, ModelName: variant.Model, SourceLang: "english", TargetLang: "korean",
                    SystemPrompt: systemPrompt, BatchSize: 12, MaxChars: 15000, MaxOutputTokens: 65536,
                    RunSampleToEstimateOutputTokens: false, IncludeCompletedItems: false,
                    GlobalGlossary: globalGlossary), cancellation.Token);
                results.Add(new { Group = group.Key, Estimate = estimate });
                continue;
            }
            try
            {
                if (stored.Count > 0) await new TranslationService(db, client).TranslateIdsAsync(new(
                    ApiKey: key, ModelName: variant.Model, SourceLang: "english", TargetLang: "korean",
                    SystemPrompt: systemPrompt, Ids: stored.Select(s => s.Id).ToArray(),
                    BatchSize: 12, MaxChars: 15000, MaxConcurrency: 2, Temperature: 0.1,
                    MaxOutputTokens: 65536, MaxRetries: 3, UseRecStyleHints: true, EnableRepairPass: true,
                    EnableSessionTermMemory: true, OnRowUpdated: null, WaitIfPaused: null,
                    CancellationToken: cancellation.Token, GlobalGlossary: globalGlossary,
                    SemanticRepairMode: PlaceholderSemanticRepairMode.Soft, KeepSkyrimTagsRaw: true,
                    EnableDialogueContextWindow: true, EnablePromptCache: false, EnableQualityEscalation: false,
                    EnableRiskyCandidateRerank: true, RiskyCandidateCount: 3,
                    EnableBookContext: variant.BookContext, BookTitlesByEdid: titles));
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine(logger.BudgetExceeded ? "STOPPED: budget cap reached." : "STOPPED: canceled.");
                exitCode = 1;
            }
            var done = await db.GetStringsAsync(groupRows.Count, 0, CancellationToken.None);
            results.AddRange(done.OrderBy(s => s.OrderIndex).Select(s => new {
                groupRows[s.OrderIndex].Id, groupRows[s.OrderIndex].Group, s.Rec, s.Edid,
                Source = s.SourceText, Dest = s.DestText, Status = s.Status.ToString(), Error = s.ErrorMessage,
            }));
            if (exitCode != 0) break;
        }

        await File.WriteAllTextAsync(Path.Combine(root, estimateOnly ? "estimate.json" : "rows.json"),
            JsonSerializer.Serialize(results, JsonOptions));
        var summary = new {
            Variant = args[1], variant.Model, Rows = rows.Count, Seconds = elapsed.Elapsed.TotalSeconds,
            Usage = logger.Summary(), SpentBeforeUsd = spentBefore, BudgetUsd = budget,
        };
        await File.WriteAllTextAsync(Path.Combine(root, "run.json"), JsonSerializer.Serialize(summary, JsonOptions));
        Console.WriteLine(JsonSerializer.Serialize(summary));
        return exitCode;
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record EvalDataset(List<EvalRow> Rows, Dictionary<string, List<EvalTerm>>? ProjectGlossaries);

    private sealed record EvalRow(string Id, string Group, string Rec, string? Edid, string Source,
        string? Reference, [property: System.Text.Json.Serialization.JsonPropertyName("book_title")] string? BookTitle);

    private sealed record EvalTerm(string Source, string Target,
        [property: System.Text.Json.Serialization.JsonPropertyName("match_mode")] int MatchMode,
        [property: System.Text.Json.Serialization.JsonPropertyName("force_mode")] int ForceMode,
        int Priority);

    private sealed class BudgetLogger(string path, double remainingUsd, CancellationTokenSource cancellation) : IGeminiCallLogger
    {
        private readonly object _gate = new();
        private double _spent;
        private int _calls, _failed, _withoutUsage;
        private long _prompt, _completion;
        public bool BudgetExceeded { get; private set; }

        public void Log(GeminiCallLogEntry entry)
        {
            lock (_gate)
            {
                File.AppendAllText(path, JsonSerializer.Serialize(new {
                    entry.StartedAt, Seconds = entry.Duration.TotalSeconds, Operation = entry.Operation.ToString(),
                    entry.ModelName, entry.StatusCode, entry.Success, entry.PromptTokens, entry.CompletionTokens,
                    entry.OutputTokens, entry.ThoughtsTokens, entry.CostUsd, entry.Purpose, entry.FinishReason,
                }) + "\n");
                if (entry.Operation != GeminiCallOperation.GenerateContent) return;
                _calls++;
                if (!entry.Success) _failed++;
                if (entry.PromptTokens == null) _withoutUsage++;
                _prompt += entry.PromptTokens ?? 0;
                _completion += entry.CompletionTokens ?? 0;
                _spent += entry.CostUsd ?? 0;
                if (_spent >= remainingUsd && !BudgetExceeded)
                {
                    BudgetExceeded = true;
                    cancellation.Cancel();
                }
            }
        }

        public object Summary()
        {
            lock (_gate)
                return new { GenerationCalls = _calls, FailedCalls = _failed, CallsWithoutUsage = _withoutUsage,
                    PromptTokens = _prompt, CompletionTokensIncludingThinking = _completion, EstimatedUsd = _spent };
        }
    }
}
