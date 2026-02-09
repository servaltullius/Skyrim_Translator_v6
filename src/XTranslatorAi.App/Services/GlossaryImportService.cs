using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services;

public sealed class GlossaryImportService
{
    private readonly GlossaryFileService _fileService;

    public GlossaryImportService(GlossaryFileService fileService)
    {
        _fileService = fileService;
    }

    public readonly record struct GlossaryImportOptions(
        int Priority,
        GlossaryMatchMode MatchMode,
        GlossaryForceMode ForceMode,
        string? Note
    );

    public readonly record struct GlossaryImportResult(int InsertedCount, int SkippedExisting, int ConflictCount);

    public async Task<GlossaryImportResult?> ImportFromFileAsync(
        ProjectDb db,
        string glossaryPath,
        GlossaryImportOptions options,
        CancellationToken cancellationToken
    )
    {
        var entries = await _fileService.ReadGlossaryEntriesAsync(glossaryPath, cancellationToken);
        if (entries.Count == 0)
        {
            return null;
        }

        var (toImport, conflictCount) = CollapseGlossaryEntriesBySource(entries);

        var existing = await db.GetGlossaryAsync(cancellationToken);
        var (rows, skippedExisting) = BuildGlossaryImportRows(
            toImport,
            existing,
            options.Priority,
            options.MatchMode,
            options.ForceMode,
            note: options.Note
        );

        if (rows.Count > 0)
        {
            await db.BulkInsertGlossaryAsync(rows, cancellationToken);
        }

        return new GlossaryImportResult(rows.Count, skippedExisting, conflictCount);
    }

    private static (List<(string? Category, string Source, string Target)> ToImport, int ConflictCount) CollapseGlossaryEntriesBySource(
        IReadOnlyList<(string? Category, string Source, string Target)> entries
    )
    {
        var bySource = entries.GroupBy(p => p.Source, StringComparer.OrdinalIgnoreCase);
        var toImport = new List<(string? Category, string Source, string Target)>();
        var conflictCount = 0;

        foreach (var group in bySource)
        {
            var distinctTargets = group.Select(g => g.Target.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinctTargets.Count != 1)
            {
                conflictCount++;
                continue;
            }

            var categories = group
                .Select(g => (g.Category ?? "").Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            string? category = categories.Count switch
            {
                0 => null,
                1 => categories[0],
                _ => string.Join(" | ", categories),
            };

            toImport.Add((category, group.Key.Trim(), distinctTargets[0]));
        }

        return (toImport, conflictCount);
    }

    private static (
        List<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> Rows,
        int SkippedExisting
    ) BuildGlossaryImportRows(
        IReadOnlyList<(string? Category, string Source, string Target)> toImport,
        IReadOnlyList<GlossaryEntry> existing,
        int priority,
        GlossaryMatchMode matchMode,
        GlossaryForceMode forceMode,
        string? note
    )
    {
        var existingSet = new HashSet<(string Source, string Target)>(new SourceTargetComparer());
        foreach (var e in existing)
        {
            existingSet.Add((e.SourceTerm, e.TargetTerm));
        }

        var rows = new List<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)>();
        var skippedExisting = 0;

        foreach (var (category, src, dst) in toImport)
        {
            var key = (src, dst);
            if (existingSet.Contains(key))
            {
                skippedExisting++;
                continue;
            }

            rows.Add(
                (
                    Category: category,
                    SourceTerm: src,
                    TargetTerm: dst,
                    Enabled: true,
                    Priority: priority,
                    MatchMode: (int)matchMode,
                    ForceMode: (int)forceMode,
                    Note: note
                )
            );
            existingSet.Add(key);
        }

        return (rows, skippedExisting);
    }

    private sealed class SourceTargetComparer : IEqualityComparer<(string Source, string Target)>
    {
        public bool Equals((string Source, string Target) x, (string Source, string Target) y)
            => string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string Target) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target ?? "")
            );
    }
}

