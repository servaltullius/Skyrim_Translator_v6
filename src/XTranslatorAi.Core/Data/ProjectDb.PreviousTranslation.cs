using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace XTranslatorAi.Core.Data;

/// <summary>
/// Translations of the same plugin fields from an earlier translated release (for example the previous
/// Korean patch of a mod), shown to the model as a reference. They are stored by plugin field key, not by
/// row id, because reopening a plugin re-imports its rows with new ids and clears row notes.
/// </summary>
public sealed partial class ProjectDb
{
    public async Task ReplacePreviousTranslationsAsync(
        string fileName,
        IReadOnlyDictionary<string, string> textByFieldKey,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await ClearPreviousTranslationsUnsafeAsync(tx, cancellationToken);

            await using var insert = _connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO PreviousTranslation (FieldKey, Text) VALUES ($FieldKey, $Text);";
            var key = insert.Parameters.Add("$FieldKey", SqliteType.Text);
            var text = insert.Parameters.Add("$Text", SqliteType.Text);
            foreach (var (fieldKey, value) in textByFieldKey)
            {
                key.Value = fieldKey;
                text.Value = value;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var source = _connection.CreateCommand();
            source.Transaction = tx;
            source.CommandText = "INSERT INTO PreviousTranslationSource (Id, FileName, ImportedAt) VALUES (1, $FileName, $ImportedAt);";
            source.Parameters.AddWithValue("$FileName", fileName);
            source.Parameters.AddWithValue("$ImportedAt", DateTimeOffset.UtcNow.ToString("O"));
            await source.ExecuteNonQueryAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearPreviousTranslationsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await ClearPreviousTranslationsUnsafeAsync(tx, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<long, string>> GetPreviousTranslationsByStringIdAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT b.StringId, p.Text
                FROM PreviousTranslation p
                JOIN PluginStringBinding b ON b.FieldKey = p.FieldKey;
                """;
            var result = new Dictionary<long, string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetInt64(0)] = reader.GetString(1);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The file the references came from and how many fields it supplied, or null when none are set.</summary>
    public async Task<(string FileName, int Count)?> GetPreviousTranslationSourceAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                "SELECT s.FileName, (SELECT COUNT(*) FROM PreviousTranslation) FROM PreviousTranslationSource s WHERE s.Id = 1;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? (reader.GetString(0), reader.GetInt32(1))
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ClearPreviousTranslationsUnsafeAsync(SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var clear = _connection.CreateCommand();
        clear.Transaction = tx;
        clear.CommandText = "DELETE FROM PreviousTranslation; DELETE FROM PreviousTranslationSource;";
        await clear.ExecuteNonQueryAsync(cancellationToken);
    }
}
