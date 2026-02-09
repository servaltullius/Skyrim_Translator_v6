using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task<NexusContextInfo?> TryGetNexusContextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT
                  GameDomain,
                  ModId,
                  ModUrl,
                  ModName,
                  Summary,
                  ContextText,
                  UpdatedAt
                FROM NexusContext
                WHERE Id = 1
                LIMIT 1;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new NexusContextInfo(
                GameDomain: reader.GetString(0),
                ModId: reader.GetInt64(1),
                ModUrl: reader.IsDBNull(2) ? null : reader.GetString(2),
                ModName: reader.IsDBNull(3) ? null : reader.GetString(3),
                Summary: reader.IsDBNull(4) ? null : reader.GetString(4),
                ContextText: reader.GetString(5),
                UpdatedAt: DateTimeOffset.Parse(
                    reader.GetString(6),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                )
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertNexusContextAsync(NexusContextInfo context, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO NexusContext (
                  Id, GameDomain, ModId, ModUrl, ModName, Summary, ContextText, UpdatedAt
                ) VALUES (
                  1, $GameDomain, $ModId, $ModUrl, $ModName, $Summary, $ContextText, $UpdatedAt
                )
                ON CONFLICT(Id) DO UPDATE SET
                  GameDomain=$GameDomain,
                  ModId=$ModId,
                  ModUrl=$ModUrl,
                  ModName=$ModName,
                  Summary=$Summary,
                  ContextText=$ContextText,
                  UpdatedAt=$UpdatedAt
                ;
                """;

            cmd.Parameters.AddWithValue("$GameDomain", context.GameDomain ?? "");
            cmd.Parameters.AddWithValue("$ModId", context.ModId);
            cmd.Parameters.AddWithValue("$ModUrl", (object?)context.ModUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ModName", (object?)context.ModName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Summary", (object?)context.Summary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ContextText", context.ContextText ?? "");
            cmd.Parameters.AddWithValue("$UpdatedAt", context.UpdatedAt.ToString("O"));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearNexusContextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM NexusContext WHERE Id = 1;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
