using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services;

public sealed class GlossaryFileService
{
    public async Task<IReadOnlyList<(string? Category, string Source, string Target)>> ReadGlossaryEntriesAsync(
        string glossaryPath,
        CancellationToken cancellationToken
    )
    {
        var text = await File.ReadAllTextAsync(glossaryPath, cancellationToken);
        return string.Equals(Path.GetExtension(glossaryPath), ".tsv", StringComparison.OrdinalIgnoreCase)
            ? ParseTsvGlossaryEntries(text)
            : GlossaryFileParser.ParseEntries(text);
    }

    public static IReadOnlyList<(string? Category, string Source, string Target)> ParseTsvGlossaryEntries(string text)
    {
        var list = new List<(string? Category, string Source, string Target)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var cols = line.Split('\t');
            if (cols.Length < 2)
            {
                continue;
            }

            // Optional header row: Source<TAB>Target
            if (cols.Length == 2
                && string.Equals(cols[0].Trim(), "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cols[1].Trim(), "Target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Optional header row: Category<TAB>Source<TAB>Target...
            if (cols.Length >= 3
                && string.Equals(cols[1].Trim(), "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cols[2].Trim(), "Target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? category;
            string source;
            string target;

            if (cols.Length == 2)
            {
                category = null;
                source = cols[0].Trim();
                target = cols[1].Trim();
            }
            else
            {
                var categoryRaw = cols[0].Trim();
                category = string.IsNullOrWhiteSpace(categoryRaw) ? null : categoryRaw;
                source = cols[1].Trim();
                target = cols[2].Trim();
            }

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            list.Add((category, source, target));
        }

        return list;
    }

    public static string BuildGlossaryTsv(
        IEnumerable<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, GlossaryMatchMode MatchMode, GlossaryForceMode ForceMode, string? Note)> entries
    )
    {
        var sb = new System.Text.StringBuilder(capacity: Math.Min(1_000_000, 64_000));
        sb.AppendLine("Category\tSource\tTarget\tEnabled\tPriority\tMatchMode\tForceMode\tNote");
        foreach (var g in entries)
        {
            sb.Append(EscapeTsv(g.Category));
            sb.Append('\t');
            sb.Append(EscapeTsv(g.SourceTerm));
            sb.Append('\t');
            sb.Append(EscapeTsv(g.TargetTerm));
            sb.Append('\t');
            sb.Append(g.Enabled ? "1" : "0");
            sb.Append('\t');
            sb.Append(g.Priority);
            sb.Append('\t');
            sb.Append(g.MatchMode);
            sb.Append('\t');
            sb.Append(g.ForceMode);
            sb.Append('\t');
            sb.AppendLine(EscapeTsv(g.Note ?? ""));
        }

        return sb.ToString();
    }

    private static string EscapeTsv(string? value)
        => (value ?? "").Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

