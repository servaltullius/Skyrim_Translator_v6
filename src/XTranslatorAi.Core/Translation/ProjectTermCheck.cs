using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// The project glossary's forced terms, which come before the Korean patch's spelling: a game, TM or patch text that
/// spells one of them otherwise is not used for a row (or a book's block), and the model translates it with the term.
/// </summary>
internal static class ProjectTermCheck
{
    /// <summary>The forced word terms of a project glossary; null when it has none.</summary>
    public static GlossaryApplier? Build(IEnumerable<GlossaryEntry> projectGlossary)
    {
        var terms = projectGlossary
            .Where(e => e.Enabled && e.ForceMode == GlossaryForceMode.ForceToken && e.MatchMode != GlossaryMatchMode.Regex)
            .ToList();
        return terms.Count == 0 ? null : new GlossaryApplier(terms);
    }

    /// <summary>Whether <paramref name="text"/> has every forced term's translation that <paramref name="source"/> calls for.</summary>
    public static bool Keeps(GlossaryApplier? terms, string source, string text)
        => terms?.Apply(source).TokenToReplacement.Values.All(term => text.Contains(term, StringComparison.Ordinal)) ?? true;
}
