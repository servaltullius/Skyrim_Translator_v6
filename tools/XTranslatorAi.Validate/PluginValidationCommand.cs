using System.Text.Json;
using XTranslatorAi.Core.Plugins;

internal static class PluginValidationCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var positional = new List<string>();
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) { positional.Add(args[i]); continue; }
                if (args[i] is not ("--report" or "--edits" or "--source-encoding" or "--metadata-encoding" or "--target-encoding" or "--strings-directory" or "--source-language"))
                    throw new ArgumentException($"Unknown option: {args[i]}");
                if (++i >= args.Length) throw new ArgumentException("Missing option value.");
                options.Add(args[i - 1], args[i]);
            }
            if (positional.Count is < 1 or > 2)
                throw new ArgumentException("Usage: --plugin input.esp [new-output-folder] [--report report.json] [--edits edits.json] [--source-encoding utf-8] [--metadata-encoding windows-1252] [--target-encoding utf-8] [--strings-directory path] [--source-language english]");
            var source = Path.GetFullPath(positional[0]);
            var document = await PluginReader.ReadAsync(source, new(SourceLanguage: options.GetValueOrDefault("--source-language", "english"),
                SourceEncoding: options.GetValueOrDefault("--source-encoding", "utf-8"), StringsDirectory: options.GetValueOrDefault("--strings-directory"),
                MetadataEncoding: options.GetValueOrDefault("--metadata-encoding", "windows-1252")), CancellationToken.None);
            PluginExportResult? exported = null;
            if (positional.Count == 2)
            {
                var edits = options.TryGetValue("--edits", out var editsPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(editsPath)) ?? throw new InvalidDataException("Empty edits document.")
                    : new Dictionary<string, string>();
                exported = await PluginWriter.ExportAsync(document, edits,
                    new(positional[1], options.GetValueOrDefault("--target-encoding", document.Info.Options.SourceEncoding)), CancellationToken.None);
            }
            if (options.TryGetValue("--report", out var reportPath))
            {
                var fullReport = Path.GetFullPath(reportPath);
                if (string.Equals(fullReport, source, StringComparison.OrdinalIgnoreCase) || File.Exists(fullReport))
                    throw new IOException("Report must use a new path separate from input.");
                Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
                await File.WriteAllTextAsync(fullReport, JsonSerializer.Serialize(new { document.Info, document.Fields, Export = exported }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                source, document.Info.Sha256, document.Info.IsLocalized, Fields = document.Fields.Count,
                Types = document.Fields.GroupBy(field => field.Rec).ToDictionary(group => group.Key, group => group.Count()),
                document.Info.Diagnostics, Export = exported,
                Note = "Internal structural verification only. Independent xEdit/game validation is separate.",
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Plugin validation failed: {ex.Message}");
            return 1;
        }
    }
}
