using System;
using System.Collections.Generic;
using System.IO;
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
        // Ordinary lowercase English words: forcing them turns "loot the place" or
        // "he let out a shout" into game jargon.
        "loot",
        "shout",
    };

    /// <summary>A batch of entries added to the built-in glossary, and of built-in targets changed, after it first shipped.</summary>
    /// <param name="Corrections">Built-in entries whose target changed: an entry still holding <c>OldTarget</c> gets the current one.</param>
    public sealed record LaterGlossaryBatch(string Version, IReadOnlyList<string> Sources,
        IReadOnlyList<(string Source, string OldTarget)> Corrections);

    /// <summary>
    /// A global glossary is filled from the built-in list only when it is created, so that entries a user
    /// deleted or edited stay that way; each batch is offered to older glossaries once
    /// (see <see cref="AddLaterEntriesOnceAsync"/>). Add a new batch with a new version instead of editing a released one.
    /// </summary>
    public static readonly IReadOnlyList<LaterGlossaryBatch> LaterAdditions = new LaterGlossaryBatch[]
    {
        // Without these, "Scroll" (주문서) is forced inside the name: "엘더 주문서".
        new("2026-10-01", new[] { "Elder Scroll", "Elder Scrolls" }, Array.Empty<(string, string)>()),
        // Forcing the single words broke these in Serana Dialogue Add-On: "길드 달인", "환영마법 마법".
        new("2026-10-03", new[] { "Guild Master", "Alteration magic", "Conjuration magic", "Destruction magic", "Illusion magic", "Restoration magic" },
            Array.Empty<(string, string)>()),
        // 몰락 발 is the more common Korean name and the one the official translation (built-in TM) uses.
        new("2026-10-03.2", Array.Empty<string>(), new[] { ("Molag Bal", "몰라그 발") }),
        // Names the official translation (built-in TM) only uses inside sentences, so the name index cannot learn them:
        // Serana Dialogue Add-On came back with 사이직 오더, 원로평의회, 아르테움, 호닝브루 and 블랙브라이어.
        new("2026-10-03.3", new[] { "Psijic Order", "Elder Council", "Black-Briar", "Artaeum", "Honningbrew" }, Array.Empty<(string, string)>()),
        // The official translation (built-in TM) names the smithing tiers 초급, 중급, 상급; 하급 for Fine broke the set.
        new("2026-10-04", Array.Empty<string>(), new[] { ("Fine", "하급") }),
        // Decided 2026-10-05 after comparing with the official translation (Iron Boots 철 전투화, Fortify Smithing 제련 강화).
        new("2026-10-05", Array.Empty<string>(), new[] { ("Boots", "부츠"), ("Smithing", "대장"), ("Fortify Smithing", "대장 강화") }),
        // Decided 2026-10-05 from the Elden Rim review ([Activate Button] 활성화 버튼, [Sprint Button] 질주 버튼).
        // [Activate] and [Sprint] alone stay English as the game's key names (PlaceholderMasker.ControlKeyNames).
        new("2026-10-05.2", new[] { "Activate Button", "Sprint Button" }, Array.Empty<(string, string)>()),
        // Decided 2026-10-05 from the Feris review: the game (built-in TM) names the cave 부풀은 사내의 암굴.
        new("2026-10-05.3", Array.Empty<string>(), new[] { ("Bloated Man's Grotto", "부풀은 사내 암굴") }),
    };

    /// <summary>
    /// Applies one <see cref="LaterAdditions"/> batch (adds its missing entries, corrects built-in targets the user
    /// has not changed) and writes <paramref name="stampPath"/>, so a later deletion or edit is not undone.
    /// </summary>
    public async Task AddLaterEntriesOnceAsync(
        ProjectDb db,
        string stampPath,
        string version,
        BethesdaFranchise franchise,
        CancellationToken cancellationToken
    )
    {
        if (File.Exists(stampPath))
        {
            return;
        }

        var batch = LaterAdditions.Single(b => b.Version == version);
        var sources = new HashSet<string>(batch.Sources, StringComparer.OrdinalIgnoreCase);
        var existing = await db.GetGlossaryAsync(cancellationToken);
        var existingSources = new HashSet<string>(existing.Select(e => e.SourceTerm.Trim()), StringComparer.OrdinalIgnoreCase);
        var builtIn = GlossaryFileParser.ParseEntries(EmbeddedAssets.LoadDefaultGlossary(franchise));
        var additions = builtIn.Where(e => sources.Contains(e.Source.Trim())).ToList();
        var rows = BuildBuiltInGlossaryInsertRows(additions, existingSources);
        if (rows.Count > 0)
        {
            await db.BulkInsertGlossaryAsync(rows, cancellationToken);
        }

        var corrections = new List<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)>();
        foreach (var entry in existing.Where(e => e.Note?.StartsWith("Built-in default glossary", StringComparison.Ordinal) == true))
        {
            if (CorrectedTarget(batch, builtIn, entry) is { } target)
            {
                corrections.Add((entry.Id, entry.Category, entry.SourceTerm, target, entry.Enabled, entry.Priority,
                    (int)entry.MatchMode, (int)entry.ForceMode, entry.Note));
            }
        }

        if (corrections.Count > 0)
        {
            await db.BulkUpdateGlossaryAsync(corrections, cancellationToken);
        }

        await File.WriteAllTextAsync(stampPath, $"added={rows.Count} corrected={corrections.Count}{Environment.NewLine}", cancellationToken);
    }

    private static string? CorrectedTarget(LaterGlossaryBatch batch, IReadOnlyList<(string? Category, string Source, string Target)> builtIn, GlossaryEntry entry)
    {
        var source = entry.SourceTerm.Trim();
        var stillOld = batch.Corrections.Any(c => string.Equals(c.Source, source, StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(c.OldTarget, entry.TargetTerm.Trim(), StringComparison.Ordinal));
        if (!stillOld)
        {
            return null;
        }

        var current = builtIn.FirstOrDefault(e => string.Equals(e.Source.Trim(), source, StringComparison.OrdinalIgnoreCase)).Target?.Trim();
        return string.IsNullOrEmpty(current) || current == entry.TargetTerm.Trim() ? null : current;
    }

    /// <summary>Stamp version of the one-time pass that updates built-in entries (<see cref="BuildBuiltInGlossaryUpdates"/>).</summary>
    public const string MigrationStampVersion = "migrations-2026-10-04";

    /// <param name="applyMigrations">
    /// Update old built-in entries (targets, categories, prompt-only defaults). The pass used to run on every open,
    /// and the entries a user edited keep the built-in note, so a user's ForceToken for Dragon, 제련 for Smithing or a
    /// cleared category came back on the next launch. Callers run it once per glossary.
    /// </param>
    public Task EnsureBuiltInGlossaryAsync(
        ProjectDb db,
        CancellationToken cancellationToken,
        bool insertMissingEntries = true,
        BethesdaFranchise franchise = BethesdaFranchise.ElderScrolls,
        bool applyMigrations = true
    )
        => EnsureBuiltInGlossaryCoreAsync(db, cancellationToken, insertMissingEntries, franchise, applyMigrations);

    private static async Task EnsureBuiltInGlossaryCoreAsync(
        ProjectDb db,
        CancellationToken cancellationToken,
        bool insertMissingEntries,
        BethesdaFranchise franchise,
        bool applyMigrations
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

        var toUpdate = applyMigrations ? BuildBuiltInGlossaryUpdates(existing, categoryByPair) : new();
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

            // Only entries never switched before: a switched one carries the prompt-only note, so a user's later
            // ForceToken for it stays.
            var updatedForceMode = e.ForceMode;
            if (ShouldDefaultGlossaryUsePromptOnly(e.SourceTerm) && updatedForceMode != GlossaryForceMode.PromptOnly
                && string.Equals(e.Note, "Built-in default glossary", StringComparison.Ordinal))
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
