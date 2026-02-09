using System;
using System.Collections.Generic;

namespace XTranslatorAi.App.Services;

public static class TranslationMemoryFileService
{
    public static List<(string SourceText, string DestText)> ParseTsvPairs(IEnumerable<string> lines)
    {
        var pairs = new List<(string SourceText, string DestText)>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 2)
            {
                continue;
            }

            var src = (parts[0] ?? "").Trim();
            var dst = (parts[1] ?? "").Trim();
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
            {
                continue;
            }

            // Optional header row: Source<TAB>Target
            if (pairs.Count == 0
                && string.Equals(src, "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(dst, "Target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            pairs.Add((src, dst));
        }

        return pairs;
    }

    public static string BuildTsv(IEnumerable<(string SourceText, string DestText)> entries)
    {
        var sb = new System.Text.StringBuilder(capacity: Math.Min(1_000_000, 64_000));
        sb.AppendLine("Source\tTarget");
        foreach (var e in entries)
        {
            sb.Append(EscapeTsv(e.SourceText));
            sb.Append('\t');
            sb.AppendLine(EscapeTsv(e.DestText));
        }

        return sb.ToString();
    }

    private static string EscapeTsv(string? value)
        => (value ?? "").Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

