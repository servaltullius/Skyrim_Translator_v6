using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task UpdateStringTranslationAsync(
        long id,
        string destText,
        StringEntryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The row and its notes change together: an edit cancelled between them left the TM's note on a 수정됨 row.
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                UPDATE StringEntry
                SET DestText=$DestText,
                    Status=$Status,
                    ErrorMessage=$ErrorMessage,
                    UpdatedAt=$UpdatedAt
                WHERE Id=$Id;
                """;

            cmd.Parameters.AddWithValue("$Id", id);
            cmd.Parameters.AddWithValue("$DestText", destText ?? "");
            cmd.Parameters.AddWithValue("$Status", (int)status);
            cmd.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // A hand edit is no longer the TM's, the game's or the earlier release's text: their notes stayed, so the next
            // run's TM refresh and reopening the project colored the row as a TM hit again, and every scan kept its "TM 대신
            // 번역" notice. A held row given a translation is held no more: the note stayed and came back with every reopen.
            if (StringNotePolicy.SavedAs(status) is { } saved) await ClearNotesUnsafeAsync(tx, id, saved, cancellationToken);

            await tx.CommitAsync(cancellationToken);
            if (status == StringEntryStatus.Edited && !string.IsNullOrWhiteSpace(destText))
            {
                await TryUpsertEditedTranslationMemoryUnsafeAsync(id, destText, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateStringTranslationsAsync(
        IReadOnlyList<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)> rows,
        CancellationToken cancellationToken
    )
    {
        if (rows.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = CreateUpdateStringTranslationsCommand(_connection, tx, out var p);
            // The notes follow the rows as one saved alone (StringNotePolicy): this save kept them all, a hand edit's TM
            // notes and a hold included, while the single save cleared them.
            var clears = new Dictionary<StringNotePolicy.RowEvent, (SqliteCommand Command, SqliteParameter Id)>();

            var now = DateTimeOffset.UtcNow.ToString("O");
            try
            {
                foreach (var row in rows)
                {
                    p.BindRow(row, now);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    if (StringNotePolicy.SavedAs(row.Status) is not { } saved) continue;
                    if (!clears.TryGetValue(saved, out var clear))
                    {
                        clear.Command = CreateClearNotesCommand(tx, saved, out clear.Id);
                        clears[saved] = clear;
                    }

                    clear.Id.Value = row.Id;
                    await clear.Command.ExecuteNonQueryAsync(cancellationToken);
                }

                await tx.CommitAsync(cancellationToken);
            }
            finally
            {
                foreach (var clear in clears.Values) await clear.Command.DisposeAsync();
            }

            var editedRows = rows
                .Where(r => r.Status == StringEntryStatus.Edited && !string.IsNullOrWhiteSpace(r.DestText))
                .Select(r => (r.Id, r.DestText))
                .ToArray();
            await UpsertTranslationMemoryForEditedRowsUnsafeAsync(editedRows, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SqliteCommand CreateUpdateStringTranslationsCommand(
        SqliteConnection connection,
        SqliteTransaction tx,
        out UpdateStringTranslationsParameters parameters
    )
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE StringEntry
            SET DestText=$DestText,
                Status=$Status,
                ErrorMessage=$ErrorMessage,
                UpdatedAt=$UpdatedAt
            WHERE Id=$Id;
            """;

        parameters = UpdateStringTranslationsParameters.Create(cmd);
        return cmd;
    }

    private sealed class UpdateStringTranslationsParameters
    {
        private UpdateStringTranslationsParameters(
            SqliteParameter id,
            SqliteParameter destText,
            SqliteParameter status,
            SqliteParameter errorMessage,
            SqliteParameter updatedAt
        )
        {
            Id = id;
            DestText = destText;
            Status = status;
            ErrorMessage = errorMessage;
            UpdatedAt = updatedAt;
        }

        public SqliteParameter Id { get; }
        public SqliteParameter DestText { get; }
        public SqliteParameter Status { get; }
        public SqliteParameter ErrorMessage { get; }
        public SqliteParameter UpdatedAt { get; }

        public static UpdateStringTranslationsParameters Create(SqliteCommand cmd)
        {
            return new UpdateStringTranslationsParameters(
                id: AddParam(cmd, "$Id"),
                destText: AddParam(cmd, "$DestText"),
                status: AddParam(cmd, "$Status"),
                errorMessage: AddParam(cmd, "$ErrorMessage"),
                updatedAt: AddParam(cmd, "$UpdatedAt")
            );
        }

        public void BindRow(
            (long Id, string DestText, StringEntryStatus Status, string? ErrorMessage) row,
            string updatedAt
        )
        {
            Id.Value = row.Id;
            DestText.Value = row.DestText ?? "";
            Status.Value = (int)row.Status;
            ErrorMessage.Value = (object?)row.ErrorMessage ?? DBNull.Value;
            UpdatedAt.Value = updatedAt;
        }

        private static SqliteParameter AddParam(SqliteCommand cmd, string name)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
            return p;
        }
    }
}
