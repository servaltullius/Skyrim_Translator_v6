using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private async Task ReloadGlossaryAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var rows = await _projectGlossaryService.GetAsync(db, CancellationToken.None);
        var list = rows
            .Select(
                g =>
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
            )
            .ToList();

        Glossary.ReplaceAll(list);
        RebuildGlossaryCategoryFilters();
        GlossaryView.Refresh();
        RebuildGlossaryLookupResults();
    }

    partial void OnGlossaryFilterTextChanged(string value) => GlossaryView.Refresh();
    partial void OnGlossaryFilterCategoryChanged(string value) => GlossaryView.Refresh();

    private bool GlossaryFilter(object obj)
    {
        if (obj is not GlossaryEntryViewModel entry)
        {
            return true;
        }

        var categoryFilter = (GlossaryFilterCategory ?? "").Trim();
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

        var q = (GlossaryFilterText ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            if ((entry.Category ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.SourceTerm ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.TargetTerm ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.Note ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    private void RebuildGlossaryCategoryFilters()
    {
        var categories = Glossary
            .Select(g => (g.Category ?? "").Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var list = new List<string> { GlossaryCategoryAll, GlossaryCategoryNone };
        list.AddRange(categories);
        GlossaryCategoryFilterValues.ReplaceAll(list);

        if (!list.Contains(GlossaryFilterCategory, StringComparer.Ordinal))
        {
            GlossaryFilterCategory = GlossaryCategoryAll;
        }
    }
}
