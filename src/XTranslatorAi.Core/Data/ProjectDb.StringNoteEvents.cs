using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace XTranslatorAi.Core.Data;

/// <summary>The notes an event takes off a row (StringNotePolicy), as the statements that change rows run it.</summary>
public sealed partial class ProjectDb
{
    /// <summary>Takes off the row the notes the event clears, inside the caller's transaction.</summary>
    private async Task ClearNotesUnsafeAsync(SqliteTransaction tx, long id, StringNotePolicy.RowEvent rowEvent, CancellationToken cancellationToken)
    {
        await using var clear = CreateClearNotesCommand(tx, rowEvent, out var idParameter);
        idParameter.Value = id;
        await clear.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A command taking off one row's notes the event clears; the row is its $Id.</summary>
    private SqliteCommand CreateClearNotesCommand(SqliteTransaction tx, StringNotePolicy.RowEvent rowEvent, out SqliteParameter id)
    {
        var groups = StringNotePolicy.ClearedBy(rowEvent);
        var kinds = StringNotePolicy.KindsOf(groups.ToArray());
        var clauses = new List<string>();
        if (kinds.Count > 0) clauses.Add("Kind IN (" + string.Join(", ", kinds.Select((_, i) => "$Kind" + i)) + ")");
        var reviewed = groups.Contains(StringNotePolicy.NoteGroup.UserMark);
        if (reviewed) clauses.Add("substr(Kind, 1, length($Reviewed)) = $Reviewed");

        var command = _connection.CreateCommand();
        command.Transaction = tx;
        // An event that clears nothing made "AND ()", which SQLite refuses: it runs as a statement that does nothing.
        command.CommandText = clauses.Count == 0
            ? "SELECT 0 WHERE $Id IS NULL;"
            : $"DELETE FROM StringNote WHERE StringId=$Id AND ({string.Join(" OR ", clauses)});";
        for (var i = 0; i < kinds.Count; i++) command.Parameters.AddWithValue("$Kind" + i, kinds[i]);
        if (reviewed) command.Parameters.AddWithValue("$Reviewed", TranslationConstants.LqaReviewedNoteKindPrefix);
        id = command.Parameters.Add("$Id", SqliteType.Integer);
        return command;
    }
}
