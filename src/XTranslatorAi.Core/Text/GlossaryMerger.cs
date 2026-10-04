using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text;

public static class GlossaryMerger
{
    /// <summary>Category of the disabled suggestions the session term memory adds to a project glossary.</summary>
    public const string SessionAutoSuggestionCategory = "Auto(Session)";

    public static IReadOnlyList<GlossaryEntry> Merge(
        IReadOnlyList<GlossaryEntry> projectGlossary,
        IReadOnlyList<GlossaryEntry>? globalGlossary
    )
    {
        if (globalGlossary == null || globalGlossary.Count == 0)
        {
            return projectGlossary;
        }

        if (projectGlossary.Count == 0)
        {
            return ReassignIds(globalGlossary);
        }

        var overriddenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projectGlossary)
        {
            // A disabled auto-learned suggestion is a candidate nobody has reviewed, not a choice to drop the
            // global entry: a name learned during a run hid the global entry for that name, and the suggestion
            // itself is off, so the name had no glossary entry at all. An entry the user turned off still overrides.
            if (!p.Enabled && string.Equals(p.Category?.Trim(), SessionAutoSuggestionCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = (p.SourceTerm ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                overriddenSources.Add(key);
            }
        }

        var merged = new List<GlossaryEntry>(capacity: globalGlossary.Count + projectGlossary.Count);

        foreach (var g in globalGlossary)
        {
            var key = (g.SourceTerm ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (overriddenSources.Contains(key))
            {
                continue;
            }

            merged.Add(g);
        }

        merged.AddRange(projectGlossary);
        return ReassignIds(merged);
    }

    private static IReadOnlyList<GlossaryEntry> ReassignIds(IReadOnlyList<GlossaryEntry> entries)
    {
        var list = new List<GlossaryEntry>(entries.Count);
        long id = 1;
        foreach (var e in entries)
        {
            list.Add(e with { Id = id++ });
        }

        return list;
    }
}

