using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

/// <summary>
/// Agent mode's held rows: 건너뜀 with an agent_hold note (TranslationConstants.AgentHoldNoteKind). Row and note change
/// in one transaction: written apart, a stop between them left a 대기 row with the note, or a 건너뜀 row without it.
/// </summary>
public sealed partial class ProjectDb
{
    /// <summary>The held rows' reasons by id (agent_hold notes; a row released keeps none).</summary>
    public Task<IReadOnlyDictionary<long, string>> GetHoldNotesAsync(CancellationToken cancellationToken)
        => GetStringNotesByKindAsync(TranslationConstants.AgentHoldNoteKind, cancellationToken);

    /// <summary>
    /// A held row: 건너뜀 with an agent_hold note. An XML's rows that came with a translation are 건너뜀 too, and a note
    /// left on a row another way changed is no hold: the export count, the list, show and edit each wrote this apart.
    /// </summary>
    public static bool IsHeld(StringEntryStatus status, long id, IReadOnlyDictionary<long, string> holdNotes)
        => status == StringEntryStatus.Skipped && holdNotes.ContainsKey(id);

    public async Task HoldStringsAsync(IReadOnlyList<(long Id, string Note)> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var update = _connection.CreateCommand();
            update.Transaction = tx;
            // A row another command finished meanwhile (a lane's submit) is not overwritten: its translation would leave the export.
            update.CommandText = "UPDATE StringEntry SET Status=$Status, ErrorMessage=NULL, UpdatedAt=$At "
                                 + "WHERE Id=$Id AND Status IN ($Pending, $InProgress, $Error, $Status);";
            update.Parameters.AddWithValue("$Status", (int)StringEntryStatus.Skipped);
            update.Parameters.AddWithValue("$Pending", (int)StringEntryStatus.Pending);
            update.Parameters.AddWithValue("$InProgress", (int)StringEntryStatus.InProgress);
            update.Parameters.AddWithValue("$Error", (int)StringEntryStatus.Error);
            await using var note = _connection.CreateCommand();
            note.Transaction = tx;
            note.CommandText = "INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt) VALUES ($Id, $Kind, $Message, $At) "
                               + "ON CONFLICT(StringId, Kind) DO UPDATE SET Message=excluded.Message, UpdatedAt=excluded.UpdatedAt;";
            note.Parameters.AddWithValue("$Kind", TranslationConstants.AgentHoldNoteKind);
            var at = DateTimeOffset.UtcNow.ToString("O");
            update.Parameters.AddWithValue("$At", at);
            note.Parameters.AddWithValue("$At", at);
            var updateId = update.Parameters.Add("$Id", SqliteType.Integer);
            var noteId = note.Parameters.Add("$Id", SqliteType.Integer);
            var message = note.Parameters.Add("$Message", SqliteType.Text);
            foreach (var (id, text) in rows)
            {
                updateId.Value = noteId.Value = id;
                message.Value = text.Length > 4000 ? text[..4000] + "…" : text;
                if (await update.ExecuteNonQueryAsync(cancellationToken) > 0) await note.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseHeldStringsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var update = _connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE StringEntry SET Status=$Status, ErrorMessage=NULL, UpdatedAt=$At WHERE Id=$Id AND Status=$Skipped;";
            update.Parameters.AddWithValue("$Status", (int)StringEntryStatus.Pending);
            update.Parameters.AddWithValue("$Skipped", (int)StringEntryStatus.Skipped);
            update.Parameters.AddWithValue("$At", DateTimeOffset.UtcNow.ToString("O"));
            var updateId = update.Parameters.Add("$Id", SqliteType.Integer);
            await using var clear = CreateClearNotesCommand(tx, StringNotePolicy.RowEvent.Released, out var clearId);
            foreach (var id in ids)
            {
                updateId.Value = clearId.Value = id;
                await update.ExecuteNonQueryAsync(cancellationToken);
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
