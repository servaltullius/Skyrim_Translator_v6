using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    /// <summary>
    /// Rows finished from a known translation (TM, the game's text, an earlier release): 완료 with the text, the notes the
    /// fill clears taken off (an old TM fallback note among them), and the row's note of where the text came from, in one
    /// transaction. Written apart, a stop between them left a filled row without its note, and agent mode's Korean
    /// release fill left the fallback note, so the check called the release's line "TM 대신 새로 번역".
    /// <paramref name="setAside"/> are the rows the known translation did not fit: they stay as they are, with the
    /// fallback note that says why, written in the same transaction.
    /// </summary>
    public async Task FillKnownTranslationsAsync(string kind, IReadOnlyList<(long Id, string Text, string Note)> filled,
        IReadOnlyList<(long Id, string Note)> setAside, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (filled.Count == 0 && setAside.Count == 0) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE StringEntry SET DestText=$Text, Status=$Done, ErrorMessage=NULL, UpdatedAt=$At WHERE Id=$Id;";
            update.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
            var at = DateTimeOffset.UtcNow.ToString("O");
            update.Parameters.AddWithValue("$At", at);
            var updateId = update.Parameters.Add("$Id", SqliteType.Integer);
            var text = update.Parameters.Add("$Text", SqliteType.Text);
            await using var clear = CreateClearNotesCommand(tx, StringNotePolicy.RowEvent.KnownFill, out var clearId);
            await using var note = _connection.CreateCommand();
            note.Transaction = tx;
            note.CommandText = "INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt) VALUES ($Id, $Kind, $Message, $At) "
                               + "ON CONFLICT(StringId, Kind) DO UPDATE SET Message=excluded.Message, UpdatedAt=excluded.UpdatedAt;";
            note.Parameters.AddWithValue("$At", at);
            var noteId = note.Parameters.Add("$Id", SqliteType.Integer);
            var noteKind = note.Parameters.Add("$Kind", SqliteType.Text);
            var message = note.Parameters.Add("$Message", SqliteType.Text);

            noteKind.Value = kind.Trim().ToLowerInvariant();
            foreach (var (id, translation, rowNote) in filled)
            {
                if (id <= 0) continue;
                updateId.Value = clearId.Value = noteId.Value = id;
                text.Value = translation;
                await update.ExecuteNonQueryAsync(cancellationToken);
                await clear.ExecuteNonQueryAsync(cancellationToken);
                if (NoteMessage(rowNote) is not { } kept) continue;
                message.Value = kept;
                await note.ExecuteNonQueryAsync(cancellationToken);
            }

            noteKind.Value = TranslationConstants.TmFallbackNoteKind;
            foreach (var (id, rowNote) in setAside)
            {
                if (id <= 0 || NoteMessage(rowNote) is not { } kept) continue;
                noteId.Value = id;
                message.Value = kept;
                await note.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
