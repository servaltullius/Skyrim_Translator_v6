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
        var list = rows.Select(MapGlossaryToViewModel).ToList();

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

        return MatchGlossaryFilter(entry, (GlossaryFilterCategory ?? "").Trim(), (GlossaryFilterText ?? "").Trim());
    }

    private void RebuildGlossaryCategoryFilters()
    {
        var list = BuildCategoryFilterValues(Glossary);
        GlossaryCategoryFilterValues.ReplaceAll(list);

        if (!list.Contains(GlossaryFilterCategory, StringComparer.Ordinal))
        {
            GlossaryFilterCategory = GlossaryCategoryAll;
        }
    }
}
