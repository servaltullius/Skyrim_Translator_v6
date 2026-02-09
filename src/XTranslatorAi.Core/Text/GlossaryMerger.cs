using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text;

public static class GlossaryMerger
{
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

