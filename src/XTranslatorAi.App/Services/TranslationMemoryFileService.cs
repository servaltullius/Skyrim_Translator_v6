using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace XTranslatorAi.App.Services;

public static class TranslationMemoryFileService
{
    private const string EscapedFormat = "XTranslatorAi-JSON-v1";

    public static List<(string SourceText, string DestText)> ParseTsvPairs(IEnumerable<string> lines)
    {
        var pairs = new List<(string SourceText, string DestText)>();
        var escaped = false;
        var firstRecord = true;
        foreach (var line in lines)
        {
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
            var source = escaped ? JsonSerializer.Deserialize<string>(parts[0]) ?? "" : parts[0];
            var target = escaped ? JsonSerializer.Deserialize<string>(parts[1]) ?? "" : parts[1];
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
