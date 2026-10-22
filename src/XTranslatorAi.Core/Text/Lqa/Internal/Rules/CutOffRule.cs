using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// A translation that stops at a separator its English does not end with. In the Flames of Coldharbour trial an
/// agent's script cut seven answers after "아트로나크 대장간 제조법 -" (for "Atronach Forge Recipe - Staff of the
/// Cold Flames"), and the run's checks and every other rule passed them.
/// </summary>
internal static class CutOffRule
{
    private static readonly char[] Separators = { '-', '–', '—', ':', ',', '(', '/', '·' };

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        var dest = destText.TrimEnd();
        var source = sourceText.TrimEnd();
        if (dest.Length == 0 || source.Length == 0
            || Array.IndexOf(Separators, dest[^1]) < 0 || Array.IndexOf(Separators, source[^1]) >= 0)
        {
            return;
        }

        // A decoration around the text ("- 끝 -" for "THE END"), and speech broken off with a dash where the English
        // trails off ("하지만 난—" for "But I..."), end there on purpose.
        var trailsOff = source.EndsWith("...", StringComparison.Ordinal) || source.EndsWith('…');
        if (dest.TrimStart()[0] == dest[^1] || trailsOff && dest[^1] is '-' or '–' or '—')
        {
            return;
        }

        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "cut_off",
                Message: $"번역이 '{dest[^1]}'에서 끝나 뒤가 잘린 것 같습니다(영어는 그렇게 끝나지 않습니다).",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
