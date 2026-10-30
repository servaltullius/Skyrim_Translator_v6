using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{

    /// <summary>
    /// Clears the translation of the given rows and marks them Pending, so the next run translates them
    /// again. Finished, failed and skipped rows are reset; manual edits only with
    /// <paramref name="includeEdited"/>. Rows already pending or in progress are left alone.
    /// TM notes are removed because the next run decides again whether TM applies, and the "원문 오류 수정" mark
    /// because the new translation follows the English again.
    /// Returns the number of rows reset.
    /// </summary>
    public async Task<int> ResetForRetranslationAsync(
        IReadOnlyCollection<long> ids,
        bool includeEdited,
        CancellationToken cancellationToken
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText =
                """
                UPDATE StringEntry
                SET DestText='',
                    Status=$Pending,
                    ErrorMessage=NULL,
                    UpdatedAt=$UpdatedAt
                WHERE Id=$Id
                  AND (Status IN ($Done, $Error, $Skipped) OR ($IncludeEdited = 1 AND Status = $Edited));
                """;
            update.Parameters.AddWithValue("$Pending", (int)StringEntryStatus.Pending);
            update.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
            update.Parameters.AddWithValue("$Error", (int)StringEntryStatus.Error);
            update.Parameters.AddWithValue("$Skipped", (int)StringEntryStatus.Skipped);
            update.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
            update.Parameters.AddWithValue("$IncludeEdited", includeEdited ? 1 : 0);
            update.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            var updateId = update.Parameters.Add("$Id", Microsoft.Data.Sqlite.SqliteType.Integer);

            // A "확인함" mark judged the old translation; the new one is checked afresh. A held row asked again is no longer held.
            await using var deleteNotes = CreateClearNotesCommand(tx, StringNotePolicy.RowEvent.Retranslate, out var deleteId);

            // A hand edit asked again keeps its old text in a note: the project TM learned the edit, and the run filled
            // it straight back (TranslationService skips the project TM's entry when it is that text).
            await using var oldEdit = _connection.CreateCommand();
            oldEdit.Transaction = tx;
            oldEdit.CommandText = "SELECT DestText FROM StringEntry WHERE Id=$Id AND Status=$Edited;";
            oldEdit.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
            var oldEditId = oldEdit.Parameters.Add("$Id", Microsoft.Data.Sqlite.SqliteType.Integer);
            await using var sentBack = _connection.CreateCommand();
            sentBack.Transaction = tx;
            sentBack.CommandText = "INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt) VALUES ($Id, $Kind, $Text, $UpdatedAt) "
                                   + "ON CONFLICT(StringId, Kind) DO UPDATE SET Message=excluded.Message, UpdatedAt=excluded.UpdatedAt;";
            sentBack.Parameters.AddWithValue("$Kind", TranslationConstants.RetranslatedEditNoteKind);
            sentBack.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            var sentBackId = sentBack.Parameters.Add("$Id", Microsoft.Data.Sqlite.SqliteType.Integer);
            var sentBackText = sentBack.Parameters.Add("$Text", Microsoft.Data.Sqlite.SqliteType.Text);

            var reset = 0;
            foreach (var id in ids)
            {
                oldEditId.Value = id;
                var editText = includeEdited ? await oldEdit.ExecuteScalarAsync(cancellationToken) as string : null;
                updateId.Value = id;
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    continue;
                }

                deleteId.Value = id;
                await deleteNotes.ExecuteNonQueryAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(editText))
                {
                    sentBackId.Value = id;
                    sentBackText.Value = editText;
                    await sentBack.ExecuteNonQueryAsync(cancellationToken);
                }

                reset++;
            }

            await tx.CommitAsync(cancellationToken);
            return reset;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetInProgressToPendingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                UPDATE StringEntry
                SET Status=$Pending,
                    ErrorMessage=NULL,
                    UpdatedAt=$UpdatedAt
                WHERE Status=$InProgress;
                """;

            cmd.Parameters.AddWithValue("$Pending", (int)StringEntryStatus.Pending);
            cmd.Parameters.AddWithValue("$InProgress", (int)StringEntryStatus.InProgress);
            cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

}
