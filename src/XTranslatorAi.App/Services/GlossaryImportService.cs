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

    /// <param name="ConflictCount">Sources the file itself gives different targets.</param>
    /// <param name="ExistingConflicts">Sources the glossary already holds with another target, which were not imported.</param>
    public readonly record struct GlossaryImportResult(int InsertedCount, int SkippedExisting, int ConflictCount,
        IReadOnlyList<string>? ExistingConflicts = null)
    {
        public int ExistingConflictCount => ExistingConflicts?.Count ?? 0;
    }

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

        // The app's own export keeps each entry's settings, including sources that deliberately have two
        // prompt-only targets (Hearthfire → 9월 / 허스파이어), so those rows are restored as they were.
        var (toImport, conflictCount) = CollapseGlossaryEntriesBySource(entries.Where(e => e.Settings == null).ToList());
        toImport.AddRange(entries.Where(e => e.Settings != null));

        var existing = await db.GetGlossaryAsync(cancellationToken);
        var (rows, skippedExisting, existingConflicts) = BuildGlossaryImportRows(toImport, existing, options);

        if (rows.Count > 0)
        {
            await db.BulkInsertGlossaryAsync(rows, cancellationToken);
        }

        return new GlossaryImportResult(rows.Count, skippedExisting, conflictCount, existingConflicts);
    }

    private static (List<GlossaryFileService.GlossaryFileEntry> ToImport, int ConflictCount) CollapseGlossaryEntriesBySource(
        IReadOnlyList<GlossaryFileService.GlossaryFileEntry> entries
    )
    {
        var bySource = entries.GroupBy(p => p.Source, StringComparer.OrdinalIgnoreCase);
        var toImport = new List<GlossaryFileService.GlossaryFileEntry>();
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

            toImport.Add(new GlossaryFileService.GlossaryFileEntry(category, group.Key.Trim(), distinctTargets[0]));
        }

        return (toImport, conflictCount);
    }

    /// <summary>
    /// A source the glossary already holds with another target is reported, not imported: a second row for the
    /// same source never applied (rows apply by priority, then length, then age, so the older row replaced the
    /// text first) while the import counted it as added. Prompt-only targets are the exception, as every one is
    /// sent to the model, so one is added beside a source that has only prompt-only targets.
    /// </summary>
    private static (
        List<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> Rows,
        int SkippedExisting,
        List<string> ExistingConflicts
    ) BuildGlossaryImportRows(
        IReadOnlyList<GlossaryFileService.GlossaryFileEntry> toImport,
        IReadOnlyList<GlossaryEntry> existing,
        GlossaryImportOptions options
    )
    {
        var existingSet = new HashSet<(string Source, string Target)>(new SourceTargetComparer());
        var promptOnlyBySource = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existing)
        {
            existingSet.Add((e.SourceTerm.Trim(), e.TargetTerm.Trim()));
            AddSource(e.SourceTerm, e.ForceMode);
        }

        var rows = new List<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)>();
        var skippedExisting = 0;
        var existingConflicts = new List<string>();

        foreach (var (category, src, dst, settings) in toImport)
        {
            var key = (src.Trim(), dst.Trim());
            if (existingSet.Contains(key))
            {
                skippedExisting++;
                continue;
            }

            var forceMode = settings?.ForceMode ?? options.ForceMode;
            if (promptOnlyBySource.TryGetValue(src.Trim(), out var onlyPromptOnly)
                && !(onlyPromptOnly && forceMode == GlossaryForceMode.PromptOnly))
            {
                existingConflicts.Add(src.Trim());
                continue;
            }

            rows.Add(
                (
                    Category: category,
                    SourceTerm: src,
                    TargetTerm: dst,
                    Enabled: settings?.Enabled ?? true,
                    Priority: settings?.Priority ?? options.Priority,
                    MatchMode: (int)(settings?.MatchMode ?? options.MatchMode),
                    ForceMode: (int)forceMode,
                    Note: settings != null ? settings.Value.Note : options.Note
                )
            );
            existingSet.Add(key);
            AddSource(src, forceMode);
        }

        return (rows, skippedExisting, existingConflicts);

        // Per source: whether every target it has is prompt-only.
        void AddSource(string source, GlossaryForceMode mode)
        {
            var trimmed = source.Trim();
            var promptOnly = mode == GlossaryForceMode.PromptOnly;
            promptOnlyBySource[trimmed] = promptOnlyBySource.TryGetValue(trimmed, out var all) ? all && promptOnly : promptOnly;
        }
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

