using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services;

public sealed class GlossaryFileService
{
    /// <summary>A term read from a glossary file. Settings are present only in the app's own TSV export.</summary>
    public readonly record struct GlossaryFileEntry(string? Category, string Source, string Target, GlossaryFileSettings? Settings = null);

    public readonly record struct GlossaryFileSettings(bool Enabled, int Priority, GlossaryMatchMode MatchMode, GlossaryForceMode ForceMode, string? Note);

    public async Task<IReadOnlyList<GlossaryFileEntry>> ReadGlossaryEntriesAsync(
        string glossaryPath,
        CancellationToken cancellationToken
    )
    {
        // Not File.ReadAllTextAsync: an Excel TSV from Korean Windows is CP949 (see ImportTextFileReader).
        var text = await ImportTextFileReader.ReadAllTextAsync(glossaryPath, cancellationToken);
        return string.Equals(Path.GetExtension(glossaryPath), ".tsv", StringComparison.OrdinalIgnoreCase)
            ? ParseTsvGlossaryEntries(text)
            : GlossaryFileParser.ParseEntries(text).Select(e => new GlossaryFileEntry(e.Category, e.Source, e.Target)).ToList();
    }

    /// <summary>
    /// Reads "Source, Target", "Category, Source, Target" or the app's own export (<see cref="BuildGlossaryTsv"/>).
    /// The export starts uncategorized rows with a tab; trimming the whole line first shifted every column
    /// left, so "Elder Scroll → 엘더스크롤" came back as source 엘더스크롤 and target "1", and the export's
    /// Enabled, Priority, MatchMode, ForceMode and Note were replaced by the import dialog's choices.
    /// </summary>
    public static IReadOnlyList<GlossaryFileEntry> ParseTsvGlossaryEntries(string text)
    {
        var list = new List<GlossaryFileEntry>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        var hasExportSettings = false;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var cols = raw.TrimEnd('\r').Split('\t').Select(c => c.Trim()).ToArray();
            if (cols.Length < 2)
            {
                continue;
            }

            // Optional header row: Source<TAB>Target
            if (cols.Length == 2
                && string.Equals(cols[0], "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cols[1], "Target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Optional header row: Category<TAB>Source<TAB>Target..., with the export's settings columns after it.
            if (cols.Length >= 3
                && string.Equals(cols[1], "Source", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cols[2], "Target", StringComparison.OrdinalIgnoreCase))
            {
                hasExportSettings = cols.Length >= 7
                    && string.Equals(cols[3], "Enabled", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cols[4], "Priority", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cols[5], "MatchMode", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cols[6], "ForceMode", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var (category, source, target) = cols.Length == 2
                ? ((string?)null, cols[0], cols[1])
                : (string.IsNullOrWhiteSpace(cols[0]) ? null : cols[0], cols[1], cols[2]);
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            list.Add(new GlossaryFileEntry(category, source, target, hasExportSettings ? TryReadExportSettings(cols) : null));
        }

        return list;
    }

    private static GlossaryFileSettings? TryReadExportSettings(string[] cols)
    {
        if (cols.Length < 7
            || !int.TryParse(cols[4], out var priority)
            || !Enum.TryParse<GlossaryMatchMode>(cols[5], ignoreCase: true, out var matchMode)
            || !Enum.TryParse<GlossaryForceMode>(cols[6], ignoreCase: true, out var forceMode))
        {
            return null;
        }

        var enabled = cols[3] is "1" || string.Equals(cols[3], "true", StringComparison.OrdinalIgnoreCase);
        var note = cols.Length > 7 && cols[7].Length > 0 ? cols[7] : null;
        return new GlossaryFileSettings(enabled, priority, matchMode, forceMode, note);
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

