using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services;

public sealed class BuiltInGlossaryService
{
    private static readonly HashSet<string> DefaultGlossaryPromptOnlySources = new(StringComparer.OrdinalIgnoreCase)
    {
        // Very common terms: forcing tokens can increase cost and, when token repair kicks in,
        // may produce odd trailing fragments (e.g., an extra "드래곤" at the end). Prefer prompt-only hints.
        "Block",
        "Dragon",
    };

    public Task EnsureBuiltInGlossaryAsync(
        ProjectDb db,
        CancellationToken cancellationToken,
        bool insertMissingEntries = true,
        BethesdaFranchise franchise = BethesdaFranchise.ElderScrolls
    )
        => EnsureBuiltInGlossaryCoreAsync(db, cancellationToken, insertMissingEntries, franchise);

    private static async Task EnsureBuiltInGlossaryCoreAsync(
        ProjectDb db,
        CancellationToken cancellationToken,
        bool insertMissingEntries,
        BethesdaFranchise franchise
    )
    {
        var existing = await db.GetGlossaryAsync(cancellationToken);
        var shouldInsertMissingEntries = insertMissingEntries;
        var existingSources = new HashSet<string>(existing.Select(e => e.SourceTerm.Trim()), StringComparer.OrdinalIgnoreCase);

        var fileText = EmbeddedAssets.LoadDefaultGlossary(franchise);
        var entries = GlossaryFileParser.ParseEntries(fileText);
        if (entries.Count == 0)
        {
            return;
        }

        var categoryByPair = BuildBuiltInGlossaryCategoryByPair(entries);

        var toUpdate = BuildBuiltInGlossaryUpdates(existing, categoryByPair);
        if (toUpdate.Count > 0)
        {
            await db.BulkUpdateGlossaryAsync(toUpdate, cancellationToken);
        }

        var rows = BuildBuiltInGlossaryInsertRows(entries, existingSources);
        if (shouldInsertMissingEntries && rows.Count > 0)
        {
            await db.BulkInsertGlossaryAsync(rows, cancellationToken);
        }
    }

    private static Dictionary<(string Source, string Target), string?> BuildBuiltInGlossaryCategoryByPair(
        IReadOnlyList<(string? Category, string Source, string Target)> entries
    )
    {
        var categoryByPair = new Dictionary<(string Source, string Target), string?>(new SourceTargetComparer());
        foreach (var entry in entries)
        {
            var src = entry.Source.Trim();
            var dst = entry.Target.Trim();
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
            {
                continue;
            }

            var cat = string.IsNullOrWhiteSpace(entry.Category) ? null : entry.Category.Trim();
            var key = (src, dst);
            if (!categoryByPair.TryGetValue(key, out var existingCat))
            {
                categoryByPair[key] = cat;
                continue;
            }

            if (string.IsNullOrWhiteSpace(existingCat))
            {
                categoryByPair[key] = cat;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(cat) && !existingCat!.Contains(cat, StringComparison.Ordinal))
            {
                categoryByPair[key] = existingCat + " | " + cat;
            }
        }

        return categoryByPair;
    }

    private static List<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> BuildBuiltInGlossaryUpdates(
        IReadOnlyList<GlossaryEntry> existing,
        IReadOnlyDictionary<(string Source, string Target), string?> categoryByPair
    )
    {
        var toUpdate = new List<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)>();
        foreach (var e in existing)
        {
            if (string.IsNullOrWhiteSpace(e.Note) || !e.Note.StartsWith("Built-in default glossary", StringComparison.Ordinal))
            {
                continue;
            }

            var updatedTarget = e.TargetTerm;
            if (TryMigrateBuiltInDefaultGlossaryTarget(e.SourceTerm, e.TargetTerm, out var migrated))
            {
                updatedTarget = migrated;
            }

            var existingCategory = string.IsNullOrWhiteSpace(e.Category) ? null : e.Category.Trim();
            var updatedCategory = existingCategory;
            if (existingCategory == null
                && categoryByPair.TryGetValue((e.SourceTerm, updatedTarget), out var cat)
                && !string.IsNullOrWhiteSpace(cat))
            {
                updatedCategory = cat;
            }

            var updatedForceMode = e.ForceMode;
            if (ShouldDefaultGlossaryUsePromptOnly(e.SourceTerm) && updatedForceMode != GlossaryForceMode.PromptOnly)
            {
                updatedForceMode = GlossaryForceMode.PromptOnly;
            }

            var updatedNote = e.Note;
            if (updatedForceMode == GlossaryForceMode.PromptOnly
                && string.Equals(e.Note, "Built-in default glossary", StringComparison.Ordinal))
            {
                updatedNote = "Built-in default glossary (prompt-only default)";
            }

            if (!string.Equals(existingCategory, updatedCategory, StringComparison.Ordinal)
                || !string.Equals(updatedTarget, e.TargetTerm, StringComparison.Ordinal)
                || updatedForceMode != e.ForceMode
                || !string.Equals(updatedNote, e.Note, StringComparison.Ordinal))
            {
                toUpdate.Add(
                    (
                        e.Id,
                        updatedCategory,
                        e.SourceTerm,
                        updatedTarget,
                        e.Enabled,
                        e.Priority,
                        (int)e.MatchMode,
                        (int)updatedForceMode,
                        updatedNote
                    )
                );
            }
        }

        return toUpdate;
    }

    private static bool TryMigrateBuiltInDefaultGlossaryTarget(string sourceTerm, string targetTerm, out string migratedTarget)
    {
        migratedTarget = "";

        // Conservative: only migrate known old→new built-in targets. This avoids overwriting user edits.
        // (Existing rows are persisted in the user's SQLite DB and do not automatically pick up asset changes.)
        if (string.Equals(sourceTerm, "Smithing", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetTerm, "제련", StringComparison.Ordinal))
        {
            migratedTarget = "대장";
            return true;
        }

        if (string.Equals(sourceTerm, "Block", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetTerm, "방어", StringComparison.Ordinal))
        {
            migratedTarget = "막기";
            return true;
        }

        return false;
    }

    private static List<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> BuildBuiltInGlossaryInsertRows(
        IReadOnlyList<(string? Category, string Source, string Target)> entries,
        IReadOnlySet<string> existingSources
    )
    {
        var rows = new List<(string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)>();
        foreach (var group in entries.GroupBy(p => p.Source, StringComparer.OrdinalIgnoreCase))
        {
            var src = group.Key.Trim();
            if (string.IsNullOrWhiteSpace(src) || existingSources.Contains(src))
            {
                continue;
            }

            var targets = group
                .Select(g => g.Target.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (targets.Count == 0)
            {
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

            if (targets.Count == 1)
            {
                rows.Add(BuildBuiltInGlossaryInsertRow(category, src, targets[0]));
                continue;
            }

            foreach (var dst in targets)
            {
                rows.Add(
                    (
                        Category: category,
                        SourceTerm: src,
                        TargetTerm: dst,
                        Enabled: true,
                        Priority: 10,
                        MatchMode: (int)GlossaryMatchMode.WordBoundary,
                        ForceMode: (int)GlossaryForceMode.PromptOnly,
                        Note: "Built-in default glossary (ambiguous)"
                    )
                );
            }
        }

        return rows;
    }

    private static (string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note) BuildBuiltInGlossaryInsertRow(
        string? category,
        string sourceTerm,
        string targetTerm
    )
    {
        var forceMode = ShouldDefaultGlossaryUsePromptOnly(sourceTerm)
            ? GlossaryForceMode.PromptOnly
            : GlossaryForceMode.ForceToken;
        var note = forceMode == GlossaryForceMode.PromptOnly
            ? "Built-in default glossary (prompt-only default)"
            : "Built-in default glossary";

        return (
            Category: category,
            SourceTerm: sourceTerm,
            TargetTerm: targetTerm,
            Enabled: true,
            Priority: 10,
            MatchMode: (int)GlossaryMatchMode.WordBoundary,
            ForceMode: (int)forceMode,
            Note: note
        );
    }

    private static bool ShouldDefaultGlossaryUsePromptOnly(string sourceTerm)
        => DefaultGlossaryPromptOnlySources.Contains((sourceTerm ?? "").Trim());

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
