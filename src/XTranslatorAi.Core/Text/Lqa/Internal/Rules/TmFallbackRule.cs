using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class TmFallbackRule
{
    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        IReadOnlyDictionary<long, string>? tmFallbackNotes,
        XTranslatorAi.Core.Translation.ReferenceNameIndex? referenceNames,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        List<LqaIssue> issues
    )
    {
        if (tmFallbackNotes == null || !tmFallbackNotes.TryGetValue(entry.Id, out var note) || string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        // MEI listed 136 such notes and none needed action. Dialogue is translated on its own on purpose (the
        // official lines use another speaker's tone), so a note on every dialogue line said nothing about the text.
        if (LqaScanner.GetRecBase(entry.Rec) is "INFO" or "DIAL")
        {
            return;
        }

        // A name that came out as the memory's own translation (Rulindil → 룰린딜): the fallback changed nothing.
        var name = sourceText.Trim();
        if (referenceNames?.FindIn(name, max: 2) is [var only]
            && string.Equals(only.Source, name, StringComparison.Ordinal)
            && string.Equals(only.Target, destText.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        // A row that says what a forced glossary term says (MEI's sailor Eris kept as 에리스, the memory's is 이리스) is
        // the user's choice.
        if (forceTokenGlossary.Any(term => string.Equals((term.SourceTerm ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)
                                           && string.Equals((term.TargetTerm ?? "").Trim(), destText.Trim(), StringComparison.Ordinal)))
        {
            return;
        }

        // The row was translated on its own instead of from TM; nothing is wrong with the text.
        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Info",
                Code: "tm_fallback",
                Message: note.Trim(),
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
