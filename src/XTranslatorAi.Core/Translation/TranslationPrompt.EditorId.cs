using System;
using System.Text;

namespace XTranslatorAi.Core.Translation;

public static partial class TranslationPrompt
{
    // EDIDs are optional identifiers, not instructions or additional source text.
    // Omit malformed/oversized values rather than sending a partial identifier.
    internal static string? NormalizeEditorIdReference(string? edid)
    {
        if (string.IsNullOrWhiteSpace(edid)) return null;
        var value = edid.Trim();
        if (value.Length > 160 || value.Contains("__XT_", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '_' and not '-' and not '.') return null;
        }
        return value;
    }

    private static void AppendEditorIdRule(StringBuilder sb)
        => sb.AppendLine("- 'edid' is an internal EditorID supplied as a secondary meaning clue. It may be stale or misleading: use it only to resolve ambiguity, never override explicit source facts, add mechanics, translate the identifier, or copy it into the output. Translate only the source text; token rules apply only to that text.");

    private static void AppendOptionalEditorId(StringBuilder sb, string? edid)
    {
        var reference = NormalizeEditorIdReference(edid);
        if (reference == null) return;
        sb.AppendLine();
        AppendEditorIdRule(sb);
        sb.AppendLine($"edid (reference only): {reference}");
    }
}
