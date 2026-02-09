using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task UpdateStringStatusAsync(
        long id,
        StringEntryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                UPDATE StringEntry
                SET Status=$Status,
                    ErrorMessage=$ErrorMessage,
                    UpdatedAt=$UpdatedAt
                WHERE Id=$Id;
                """;

            cmd.Parameters.AddWithValue("$Id", id);
            cmd.Parameters.AddWithValue("$Status", (int)status);
            cmd.Parameters.AddWithValue("$ErrorMessage", (object?)errorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateStringStatusesAsync(
        IReadOnlyList<long> ids,
        StringEntryStatus status,
        string? errorMessage,
        CancellationToken cancellationToken
    )
    {
        if (ids.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = CreateUpdateStringStatusesCommand(
                _connection,
                tx,
                status,
                errorMessage,
                out var p
            );

            foreach (var id in ids)
            {
                p.Id.Value = id;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SqliteCommand CreateUpdateStringStatusesCommand(
        SqliteConnection connection,
        SqliteTransaction tx,
        StringEntryStatus status,
        string? errorMessage,
        out UpdateStringStatusesParameters parameters
    )
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE StringEntry
            SET Status=$Status,
                ErrorMessage=$ErrorMessage,
                UpdatedAt=$UpdatedAt
            WHERE Id=$Id;
            """;

        parameters = UpdateStringStatusesParameters.Create(cmd, status, errorMessage);
        return cmd;
    }

    private sealed class UpdateStringStatusesParameters
    {
        private UpdateStringStatusesParameters(SqliteParameter id)
        {
            Id = id;
        }

        public SqliteParameter Id { get; }

        public static UpdateStringStatusesParameters Create(SqliteCommand cmd, StringEntryStatus status, string? errorMessage)
        {
            var id = AddParam(cmd, "$Id");

            var statusP = AddParam(cmd, "$Status");
            statusP.Value = (int)status;

            var errorP = AddParam(cmd, "$ErrorMessage");
            errorP.Value = (object?)errorMessage ?? DBNull.Value;

            var updatedAtP = AddParam(cmd, "$UpdatedAt");
            updatedAtP.Value = DateTimeOffset.UtcNow.ToString("O");

            return new UpdateStringStatusesParameters(id);
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
