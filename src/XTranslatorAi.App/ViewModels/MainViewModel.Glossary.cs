using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private static readonly HashSet<string> DefaultGlossaryPromptOnlySources = new(StringComparer.OrdinalIgnoreCase)
    {
        // Very common terms: forcing tokens can increase cost and, when token repair kicks in,
        // may produce odd trailing fragments (e.g., an extra "드래곤" at the end). Prefer prompt-only hints.
        "Block",
        "Dragon",
    };

    private static bool ShouldDefaultGlossaryUsePromptOnly(string sourceTerm)
        => DefaultGlossaryPromptOnlySources.Contains((sourceTerm ?? "").Trim());

    private static GlossaryEntryViewModel MapGlossaryToViewModel(GlossaryEntry g)
    {
        var vm = new GlossaryEntryViewModel(g.Id);
        vm.BeginUpdate();
        vm.Category = g.Category ?? "";
        vm.SourceTerm = g.SourceTerm;
        vm.TargetTerm = g.TargetTerm;
        vm.Enabled = g.Enabled;
        vm.MatchMode = g.MatchMode;
        vm.ForceMode = g.ForceMode;
        vm.Priority = g.Priority;
        vm.Note = g.Note;
        vm.EndUpdate();
        vm.MarkClean();
        return vm;
    }

    private static bool MatchGlossaryFilter(GlossaryEntryViewModel entry, string categoryFilter, string textFilter)
    {
        if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != GlossaryCategoryAll)
        {
            if (categoryFilter == GlossaryCategoryNone)
            {
                if (!string.IsNullOrWhiteSpace(entry.Category))
                {
                    return false;
                }
            }
            else
            {
                if (!string.Equals(entry.Category?.Trim() ?? "", categoryFilter, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(textFilter))
        {
            return (entry.Category ?? "").Contains(textFilter, StringComparison.OrdinalIgnoreCase)
                || (entry.SourceTerm ?? "").Contains(textFilter, StringComparison.OrdinalIgnoreCase)
                || (entry.TargetTerm ?? "").Contains(textFilter, StringComparison.OrdinalIgnoreCase)
                || (entry.Note ?? "").Contains(textFilter, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static List<string> BuildCategoryFilterValues(IEnumerable<GlossaryEntryViewModel> glossary)
    {
        var categories = glossary
            .Select(g => (g.Category ?? "").Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var list = new List<string> { GlossaryCategoryAll, GlossaryCategoryNone };
        list.AddRange(categories);
        return list;
    }
}
