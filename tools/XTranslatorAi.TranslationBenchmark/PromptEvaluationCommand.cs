using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Translation;

// Compare frozen input batches with builds from before/after a prompt change.
// This is a single-pass prompt evaluation, not an ESP export or a quality score.
internal static class PromptEvaluationCommand
{
    private const string Model = "gemini-3.8-flash";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<int> RunAsync(string inputPath, string outputPath)
    {
        var root = Path.GetFullPath(outputPath);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var ct = cancellation.Token;
        try
        {
            var inputBytes = await File.ReadAllBytesAsync(inputPath, ct);
            var batches = JsonSerializer.Deserialize<List<EvaluationBatch>>(inputBytes, JsonOptions)
                ?? throw new InvalidDataException("Missing input batches.");
            if (batches.Count is < 1 or > 15 || batches.Any(b => b.Items.Count is < 1 or > 20)
                || batches.Sum(b => b.Items.Count) > 150
                || batches.SelectMany(b => b.Items).Select(i => i.Id).Distinct().Count() != batches.Sum(b => b.Items.Count))
                throw new InvalidDataException("Use 1-15 batches, at most 150 unique rows, and at most 20 rows per batch.");
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidDataException("Use a new, empty evaluation folder.");
            Directory.CreateDirectory(root);
            using var stream = typeof(SystemPromptBuilder).Assembly.GetManifestResourceStream("SkyrimPrompt")!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var systemPrompt = new SystemPromptBuilder().Build(await reader.ReadToEndAsync(ct), false, null, false, null);
            var userPrompts = batches.Select(b => TranslationPrompt.BuildUserPrompt("english", "korean", b.Items,
                b.Glossary.Select(g => (g.Source, g.Target)).ToArray())).ToArray();
            await File.WriteAllTextAsync(Path.Combine(root, "system-prompt.txt"), systemPrompt, ct);
            await File.WriteAllBytesAsync(Path.Combine(root, "input.json"), inputBytes, ct);
            await File.WriteAllTextAsync(Path.Combine(root, "provenance.json"), JsonSerializer.Serialize(new {
                Model, Thinking = "low", Temperature = (double?)null,
                InputSha256 = Hash(inputBytes), SystemPromptSha256 = Hash(Encoding.UTF8.GetBytes(systemPrompt)),
                CoreAssemblySha256 = Hash(await File.ReadAllBytesAsync(typeof(TranslationPrompt).Assembly.Location, ct)),
                UserPromptSha256 = userPrompts.Select(p => Hash(Encoding.UTF8.GetBytes(p))).ToArray(),
                Rows = batches.Sum(b => b.Items.Count), Batches = batches.Count,
                SessionTermLearning = false, GlobalTm = false, PromptCache = false,
                Retries = 0, Repair = false, PluginExport = false,
                Note = "Frozen per-batch reference glossary and context. No new session learning, post-editing or pipeline repair."
            }, JsonOptions), ct);

            var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY")?.Trim();
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("GEMINI_API_KEY is missing.");
            var logger = new UsageLogger(Path.Combine(root, "usage.jsonl"));
            using var handler = new SafeApiHandler(Path.Combine(root, "requests.jsonl"));
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            var client = new GeminiClient(http, logger);
            var models = await client.ListModelsAsync(key, ct);
            if (!models.Any(m => m.Name == "models/" + Model && m.SupportedGenerationMethods?.Contains("generateContent") == true))
                throw new InvalidOperationException("The exact model is unavailable; no substitute will be used.");

            var results = new List<object>();
            for (var index = 0; index < batches.Count; index++)
            {
                var batch = batches[index];
                var request = new GeminiGenerateContentRequest(
                    Contents: [new("user", [new(userPrompts[index])])], CachedContent: null,
                    SystemInstruction: new(null, [new(systemPrompt)]),
                    GenerationConfig: new(null, 65536, "application/json", TranslationPrompt.BuildResponseSchema(), new(null, "low")),
                    SafetySettings: null) { Purpose = "prompt-evaluation" };
                await File.WriteAllTextAsync(Path.Combine(root, $"user-prompt-{index:00}.txt"), userPrompts[index], ct);
                var generated = await client.GenerateContentWithUsageAsync(key, Model, request, ct);
                await File.WriteAllTextAsync(Path.Combine(root, $"response-{index:00}.json"), generated.Text, ct);
                var translations = TranslationResultParser.ParseTranslations(generated.Text);
                var expectedIds = batch.Items.Select(i => i.Id).ToHashSet();
                var unexpectedIds = translations.Keys.Where(id => !expectedIds.Contains(id)).ToArray();
                foreach (var item in batch.Items)
                {
                    translations.TryGetValue(item.Id, out var text);
                    results.Add(new {
                        Batch = index, item.Id, item.Text, item.Rec, item.Ctx, item.Style, item.Edid,
                        Translation = text, MissingOrEmpty = string.IsNullOrWhiteSpace(text),
                        TokensPreserved = text != null && Tokens(item.Text).SequenceEqual(Tokens(text)),
                        UnexpectedIds = unexpectedIds
                    });
                }
                await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, JsonOptions), ct);
                await File.WriteAllTextAsync(Path.Combine(root, "usage-summary.json"), JsonSerializer.Serialize(logger.Summary(), JsonOptions), ct);
                Console.WriteLine($"Prompt evaluation: {index + 1}/{batches.Count} batches; {results.Count} rows.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new {
                Error = ex.GetType().Name,
                StatusCode = ex is GeminiHttpException h ? (int?)h.StatusCode : null,
                Detail = ex is GeminiException or HttpRequestException ? "API request failed; credentials omitted." : ex.Message
            }));
            return 1;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static IEnumerable<string> Tokens(string text) => Regex.Matches(text, @"__XT_[A-Za-z0-9_]+__")
        .Select(m => m.Value).OrderBy(t => t, StringComparer.Ordinal);

    private sealed record EvaluationBatch(List<TranslationItem> Items, List<EvaluationGlossary> Glossary);
    private sealed record EvaluationGlossary(string Source, string Target);
}
