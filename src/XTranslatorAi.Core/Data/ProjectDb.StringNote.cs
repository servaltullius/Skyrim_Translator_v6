using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task UpsertStringNoteAsync(long stringId, string kind, string message, CancellationToken cancellationToken)
    {
        if (stringId <= 0 || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalizedKind = kind.Trim().ToLowerInvariant();
        var trimmedMessage = message.Trim();
        if (trimmedMessage.Length > 4000)
        {
            trimmedMessage = trimmedMessage[..4000] + "…";
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt)
                VALUES ($StringId, $Kind, $Message, $UpdatedAt)
                ON CONFLICT(StringId, Kind) DO UPDATE SET
                  Message=excluded.Message,
                  UpdatedAt=excluded.UpdatedAt
                ;
                """;

            cmd.Parameters.AddWithValue("$StringId", stringId);
            cmd.Parameters.AddWithValue("$Kind", normalizedKind);
            cmd.Parameters.AddWithValue("$Message", trimmedMessage);
            cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteStringNoteAsync(long stringId, string kind, CancellationToken cancellationToken)
    {
        if (stringId <= 0 || string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        var normalizedKind = kind.Trim().ToLowerInvariant();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM StringNote WHERE StringId=$StringId AND Kind=$Kind;";
            cmd.Parameters.AddWithValue("$StringId", stringId);
            cmd.Parameters.AddWithValue("$Kind", normalizedKind);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteStringNotesByKindAsync(string kind, CancellationToken cancellationToken)
    {
        var normalizedKind = (kind ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedKind))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM StringNote WHERE Kind=$Kind;";
            cmd.Parameters.AddWithValue("$Kind", normalizedKind);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<long, string>> GetStringNotesByKindAsync(string kind, CancellationToken cancellationToken)
    {
        var normalizedKind = (kind ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedKind))
        {
            return new Dictionary<long, string>();
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dict = new Dictionary<long, string>();
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT StringId, Message
                FROM StringNote
                WHERE Kind=$Kind
                ORDER BY UpdatedAt DESC;
                """;
            cmd.Parameters.AddWithValue("$Kind", normalizedKind);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetInt64(0);
                var msg = reader.GetString(1);
                if (id > 0 && !dict.ContainsKey(id))
                {
                    dict[id] = msg ?? "";
                }
            }

            return dict;
        }
        finally
        {
            _gate.Release();
        }
    }
}
