using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace XTranslatorAi.App.Services;

public static class TranslationMemoryFileService
{
    private const string EscapedFormat = "XTranslatorAi-JSON-v1";

    /// <summary>
    /// A bundled seed's changes ("Source / Target / Previous", see BundledFranchiseTmSeedService), or null for any other
    /// TSV. Previous is the text the earlier seed gave the source.
    /// </summary>
    public static List<(string SourceText, string DestText, string PreviousText)>? ParseTsvChanges(IReadOnlyList<string> lines)
    {
        var header = lines.Count > 0 ? lines[0].TrimStart('\uFEFF').Split('\t') : Array.Empty<string>();
        if (header.Length != 3 || header[0].Trim() != "Source" || header[1].Trim() != "Target" || header[2].Trim() != "Previous")
        {
            return null;
        }

        var changes = new List<(string SourceText, string DestText, string PreviousText)>();
        for (var i = 1; i < lines.Count; i++)
        {
            var parts = lines[i].Split('\t');
            if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            {
                changes.Add((parts[0], parts[1], parts[2]));
            }
        }

        return changes;
    }

    public static List<(string SourceText, string DestText)> ParseTsvPairs(IEnumerable<string> lines)
    {
        var pairs = new List<(string SourceText, string DestText)>();
        var escaped = false;
        var firstRecord = true;
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            if (firstRecord && string.Equals(parts[0].Trim().TrimStart('\uFEFF'), "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[1].Trim(), "Target", StringComparison.OrdinalIgnoreCase))
            {
                escaped = parts.Length > 2 && parts[2] == EscapedFormat;
                firstRecord = false;
                continue;
            }
            firstRecord = false;
            string source, target;
            try
            {
                source = escaped ? JsonSerializer.Deserialize<string>(parts[0]) ?? "" : parts[0];
                target = escaped ? JsonSerializer.Deserialize<string>(parts[1]) ?? "" : parts[1];
            }
            catch (JsonException ex)
            {
                throw new JsonException($"{number}번째 줄", ex);
            }
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
                pairs.Add((source, target));
        }
        return pairs;
    }

    /// <summary>
    /// Versioned TSV with JSON string fields preserves tabs, line endings, quotes and backslashes.
    /// Unversioned Source/Target TSV imports retain their original literal interpretation.
    /// </summary>
    public static string BuildTsv(IEnumerable<(string SourceText, string DestText)> entries)
    {
        var builder = new StringBuilder();
        builder.Append("Source\tTarget\t").AppendLine(EscapedFormat);
        foreach (var (source, target) in entries)
        {
            builder.Append(JsonSerializer.Serialize(source ?? ""));
            builder.Append('\t');
            builder.AppendLine(JsonSerializer.Serialize(target ?? ""));
        }
        return builder.ToString();
    }
}
