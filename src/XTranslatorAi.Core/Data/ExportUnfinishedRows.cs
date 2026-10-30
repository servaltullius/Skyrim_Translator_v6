using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

/// <summary>
/// The rows an export writes without a translation of their own, counted alike for the GUI's 저장 확인 and the agent's
/// export: the agent's export said nothing of them (only status and the run's end did), and the GUI's question left out
/// the lines agent mode held, which went out in English without a word. A held row is 건너뜀 with an agent_hold note,
/// counted by the note: an XML's rows that came in with a translation are 건너뜀 too, and finished.
/// </summary>
/// <param name="Emptied">Hand edits left empty: an XML writes them empty, a plugin's writer refuses them (every field needs text).</param>
public sealed record ExportUnfinishedRows(int Errors, int Pending, int Emptied, int Held)
{
    public int Unfinished => Errors + Pending;

    public bool Any => Unfinished + Emptied + Held > 0;

    public static async Task<ExportUnfinishedRows> CountAsync(
        ProjectDb db, IEnumerable<(long Id, StringEntryStatus Status, string SourceText, string DestText)> rows, CancellationToken ct)
    {
        var holds = await db.GetHoldNotesAsync(ct);
        int errors = 0, pending = 0, emptied = 0, held = 0;
        foreach (var (id, status, source, dest) in rows)
        {
            if (status == StringEntryStatus.Error) errors++;
            else if (status.IsUnfinished()) pending++;
            else if (status == StringEntryStatus.Edited && string.IsNullOrWhiteSpace(dest) && !string.IsNullOrWhiteSpace(source)) emptied++;
            else if (ProjectDb.IsHeld(status, id, holds)) held++;
        }

        return new ExportUnfinishedRows(errors, pending, emptied, held);
    }
}
