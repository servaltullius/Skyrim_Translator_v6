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
/// The English of the earlier release (PreviousSource) may be linked too: where it equals the current English, the
/// earlier translation is reused as it is instead of being sent to the model.
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

    public async Task<IReadOnlyDictionary<long, string>> GetPluginFieldKeysByStringIdAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT StringId, FieldKey FROM PluginStringBinding;";
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

    public async Task<(IReadOnlyList<string> Masters, string PluginFileName)?> GetPluginMastersAsync(CancellationToken cancellationToken)
        => await TryGetPluginSourceAsync(cancellationToken) is { } source
            ? (source.Info.Masters, System.IO.Path.GetFileName(source.Info.InputPath))
            : null;

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

    /// <summary>
    /// Links the English of the earlier release by field key. It belongs to the linked earlier translation, so linking
    /// another earlier translation, or clearing it, drops it too.
    /// </summary>
    public async Task ReplacePreviousSourcesAsync(
        string fileName,
        IReadOnlyDictionary<string, string> sourceByFieldKey,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using (var clear = _connection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM PreviousSource; DELETE FROM PreviousSourceFile;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var insert = _connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO PreviousSource (FieldKey, Text) VALUES ($FieldKey, $Text);";
            var key = insert.Parameters.Add("$FieldKey", SqliteType.Text);
            var text = insert.Parameters.Add("$Text", SqliteType.Text);
            foreach (var (fieldKey, value) in sourceByFieldKey)
            {
                key.Value = fieldKey;
                text.Value = value;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var source = _connection.CreateCommand();
            source.Transaction = tx;
            source.CommandText = "INSERT INTO PreviousSourceFile (Id, FileName, ImportedAt) VALUES (1, $FileName, $ImportedAt);";
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

    /// <summary>
    /// The earlier translations to reuse as they are, by row id: rows whose English is the same as the earlier
    /// release's English (compared exactly, as the plugins store it).
    /// </summary>
    public async Task<IReadOnlyDictionary<long, string>> GetReusablePreviousTranslationsByStringIdAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT b.StringId, p.Text
                FROM PreviousTranslation p
                JOIN PreviousSource o ON o.FieldKey = p.FieldKey
                JOIN PluginStringBinding b ON b.FieldKey = p.FieldKey
                JOIN StringEntry s ON s.Id = b.StringId
                WHERE o.Text = s.SourceText;
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

    /// <summary>The earlier release's English file, or null when none is linked.</summary>
    public async Task<string?> GetPreviousSourceFileNameAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT FileName FROM PreviousSourceFile WHERE Id = 1;";
            return await cmd.ExecuteScalarAsync(cancellationToken) as string;
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
        clear.CommandText = "DELETE FROM PreviousTranslation; DELETE FROM PreviousTranslationSource; "
            + "DELETE FROM PreviousSource; DELETE FROM PreviousSourceFile;";
        await clear.ExecuteNonQueryAsync(cancellationToken);
    }
}
