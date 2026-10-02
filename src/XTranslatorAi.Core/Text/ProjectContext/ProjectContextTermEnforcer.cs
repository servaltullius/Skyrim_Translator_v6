using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.ProjectContext;

/// <summary>
/// The generated project context is told to copy glossary targets, but the model still wrote "Parry: 패링"
/// for a term whose glossary target is 패리. The translation follows the context, so terminology entries for
/// terms with a known target are corrected before the context is saved.
/// </summary>
public static class ProjectContextTermEnforcer
{
    public static string Apply(string context, IReadOnlyList<ProjectContextTermInfo> terms)
    {
        if (string.IsNullOrEmpty(context))
        {
            return context;
        }

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term.Target))
            {
                continue;
            }

            // "- Parry: 패링", "Parry => 패링" or inline "[주요 용어] - Parry: 패링 | - Weapon Art: 전기".
            var entry = new Regex(
                @"(?<head>(?:^|[\n|\]])[ \t]*(?:[-*•][ \t]*)?)" + Regex.Escape(term.Source)
                + @"(?<sep>[ \t]*(?::|=>|→|->)[ \t]*)(?<target>[^\n|]*?)(?<tail>[ \t]*(?=$|[\n|]))",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant
            );
            context = entry.Replace(context, m =>
                string.Equals(m.Groups["target"].Value.Trim(), term.Target.Trim(), StringComparison.Ordinal)
                    ? m.Value
                    : m.Groups["head"].Value + term.Source + m.Groups["sep"].Value + term.Target.Trim() + m.Groups["tail"].Value);
        }

        return context;
    }
}
