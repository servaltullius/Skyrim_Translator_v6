using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task<ProjectContextInfo?> TryGetProjectContextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT ContextText, UpdatedAt
                FROM ProjectContext
                WHERE Id = 1
                LIMIT 1;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ProjectContextInfo(
                ContextText: reader.GetString(0),
                UpdatedAt: DateTimeOffset.Parse(reader.GetString(1))
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertProjectContextAsync(string contextText, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO ProjectContext (Id, ContextText, UpdatedAt)
                VALUES (1, $ContextText, $UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                  ContextText=excluded.ContextText,
                  UpdatedAt=excluded.UpdatedAt;
                """;

            cmd.Parameters.AddWithValue("$ContextText", contextText ?? "");
            cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearProjectContextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM ProjectContext WHERE Id = 1;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}

