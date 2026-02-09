using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    partial void OnGlossaryLookupTextChanged(string value) => RebuildGlossaryLookupResults();
    partial void OnGlossaryLookupIncludeProjectChanged(bool value) => RebuildGlossaryLookupResults();
    partial void OnGlossaryLookupIncludeGlobalChanged(bool value) => RebuildGlossaryLookupResults();

    private void RebuildGlossaryLookupResults()
    {
        var q = (GlossaryLookupText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            GlossaryLookupResults.Clear();
            GlossaryLookupResultsView.Refresh();
            return;
        }

        var results = new List<GlossaryLookupResultViewModel>();

        if (GlossaryLookupIncludeProject)
        {
            foreach (var entry in Glossary)
            {
                if (entry == null || !MatchesGlossaryQuery(entry, q))
                {
                    continue;
                }

                results.Add(CreateGlossaryLookupResult(scope: "Project", entry));
            }
        }

        if (GlossaryLookupIncludeGlobal)
        {
            foreach (var entry in GlobalGlossary)
            {
                if (entry == null || !MatchesGlossaryQuery(entry, q))
                {
                    continue;
                }

                results.Add(CreateGlossaryLookupResult(scope: "Global", entry));
            }
        }

        results = results
            .OrderByDescending(r => r.Enabled)
            .ThenByDescending(r => r.Priority)
            .ThenBy(r => r.SourceTerm ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TargetTerm ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        GlossaryLookupResults.ReplaceAll(results);
        GlossaryLookupResultsView.Refresh();
    }

    private static bool MatchesGlossaryQuery(GlossaryEntryViewModel entry, string q)
    {
        return ContainsIgnoreCase(entry.Category ?? "", q)
               || ContainsIgnoreCase(entry.SourceTerm ?? "", q)
               || ContainsIgnoreCase(entry.TargetTerm ?? "", q)
               || ContainsIgnoreCase(entry.Note ?? "", q);
    }

    private static GlossaryLookupResultViewModel CreateGlossaryLookupResult(string scope, GlossaryEntryViewModel entry)
    {
        return new GlossaryLookupResultViewModel(
            Scope: scope,
            Category: entry.Category ?? "",
            SourceTerm: entry.SourceTerm ?? "",
            TargetTerm: entry.TargetTerm ?? "",
            Enabled: entry.Enabled,
            MatchMode: entry.MatchMode,
            ForceMode: entry.ForceMode,
            Priority: entry.Priority,
            Note: entry.Note ?? ""
        );
    }
}
