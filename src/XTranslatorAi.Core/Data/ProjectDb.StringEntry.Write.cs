using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task ClearStringsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM StringEntry;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetNonEditedTranslationsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                UPDATE StringEntry
                SET DestText='',
                    Status=$Pending,
                    ErrorMessage=NULL,
                    UpdatedAt=$UpdatedAt
                WHERE Status != $Edited;
                """;

            cmd.Parameters.AddWithValue("$Pending", (int)StringEntryStatus.Pending);
            cmd.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
            cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
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
