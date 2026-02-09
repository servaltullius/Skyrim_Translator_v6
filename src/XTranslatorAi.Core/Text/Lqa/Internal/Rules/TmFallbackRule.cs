using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class TmFallbackRule
{
    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        IReadOnlyDictionary<long, string>? tmFallbackNotes,
        List<LqaIssue> issues
    )
    {
        if (tmFallbackNotes == null || !tmFallbackNotes.TryGetValue(entry.Id, out var note) || string.IsNullOrWhiteSpace(note))
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
                Code: "tm_fallback",
                Message: note.Trim(),
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
