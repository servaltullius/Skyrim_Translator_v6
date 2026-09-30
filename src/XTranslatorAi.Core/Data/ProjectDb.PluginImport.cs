using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Core.Data;

public sealed record PluginProjectSource(PluginSourceInfo Info, string TargetEncoding);

public sealed partial class ProjectDb
{
    public async Task<PluginProjectSource?> TryGetPluginSourceAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await ReadPluginSourceAsync(null, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<PluginProjectSource?> ReadPluginSourceAsync(SqliteTransaction? transaction, CancellationToken ct)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Kind, SourceInfoJson, TargetEncoding FROM ProjectSource WHERE ProjectId=1;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        if (reader.GetString(0) != "plugin") throw new InvalidDataException("Unknown project source kind.");
        var info = JsonSerializer.Deserialize<PluginSourceInfo>(reader.GetString(1))
            ?? throw new InvalidDataException("Missing plugin source metadata.");
        return new PluginProjectSource(info, reader.GetString(2));
    }

    /// <summary>Atomically replaces plugin strings, bindings and project metadata. XML import identities are never used.</summary>
    public async Task<IReadOnlyList<StringEntry>> ReplaceImportedPluginStringsAsync(
        PluginSourceInfo source, IReadOnlyList<PluginField> fields, ProjectInfo project,
        string targetEncoding, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            var oldSource = await ReadPluginSourceAsync(tx, cancellationToken);
            var saved = new Dictionary<string, (string Source, string Dest, StringEntryStatus Status)>(StringComparer.Ordinal);
            await using (var query = _connection.CreateCommand())
            {
                query.Transaction = tx;
                query.CommandText = oldSource == null
                    ? "SELECT COUNT(*) FROM StringEntry;"
                    : "SELECT b.FieldKey,s.SourceText,s.DestText,s.Status FROM PluginStringBinding b JOIN StringEntry s ON s.Id=b.StringId WHERE s.Status IN ($Done,$Edited,$Skipped);";
                if (oldSource == null)
                {
                    if (Convert.ToInt64(await query.ExecuteScalarAsync(cancellationToken)) != 0)
                        throw new InvalidOperationException("Cannot import a plugin into an XML project.");
                }
                else
                {
                    query.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
                    query.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
                    query.Parameters.AddWithValue("$Skipped", (int)StringEntryStatus.Skipped);
                    await using var reader = await query.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var status = (StringEntryStatus)reader.GetInt32(3);
                        var dest = reader.GetString(2);
                        if (status == StringEntryStatus.Edited || !string.IsNullOrWhiteSpace(dest))
                            saved.Add(reader.GetString(0), (reader.GetString(1), dest, status));
                    }
                }
            }

            await using (var clear = _connection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM PluginStringBinding; DELETE FROM StringNote; DELETE FROM StringEntry;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var insert = CreateBulkInsertStringsCommand(_connection, tx, out var parameters);
            await using var binding = _connection.CreateCommand();
            binding.Transaction = tx;
            binding.CommandText = "INSERT INTO PluginStringBinding(StringId,FieldKey,FieldJson) VALUES(last_insert_rowid(),$key,$field) RETURNING StringId;";
            var keyParameter = binding.Parameters.Add("$key", SqliteType.Text);
            var fieldParameter = binding.Parameters.Add("$field", SqliteType.Text);
            var imported = new List<StringEntry>(fields.Count);
            var updatedAt = DateTimeOffset.UtcNow;
            var now = updatedAt.ToString("O");
            foreach (var field in fields)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dest = field.SourceText;
                var status = StringEntryStatus.Pending;
                if (saved.TryGetValue(field.Key, out var previous)
                    && string.Equals(previous.Source, field.SourceText, StringComparison.Ordinal))
                {
                    dest = previous.Dest;
                    status = previous.Status;
                }
                parameters.BindRow((field.OrderIndex, null, null, null, field.EditorId, field.Rec,
                    field.SourceText, dest, status, ""), now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                keyParameter.Value = field.Key;
                fieldParameter.Value = JsonSerializer.Serialize(field);
                var id = Convert.ToInt64(await binding.ExecuteScalarAsync(cancellationToken));
                imported.Add(new StringEntry(id, field.OrderIndex, null, null, null, field.EditorId,
                    field.Rec, field.SourceText, dest, status, null, updatedAt));
            }
            await using (var saveProject = _connection.CreateCommand())
            {
                saveProject.Transaction = tx;
                ConfigureUpsertProjectCommand(saveProject, project, DateTimeOffset.UtcNow);
                await saveProject.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var saveSource = _connection.CreateCommand())
            {
                saveSource.Transaction = tx;
                saveSource.CommandText = "INSERT INTO ProjectSource(ProjectId,Kind,SourceInfoJson,TargetEncoding) VALUES(1,'plugin',$info,$encoding) ON CONFLICT(ProjectId) DO UPDATE SET Kind='plugin',SourceInfoJson=$info,TargetEncoding=$encoding;";
                saveSource.Parameters.AddWithValue("$info", JsonSerializer.Serialize(source));
                saveSource.Parameters.AddWithValue("$encoding", targetEncoding);
                await saveSource.ExecuteNonQueryAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await tx.CommitAsync(cancellationToken);
            // Return the exact committed rows so callers can adopt without another fallible/cancelable DB read.
            return imported;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Reads one immutable translation snapshot; unfinished/skipped rows preserve original plugin bytes.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetPluginTranslationsForExportAsync(
        string expectedSourceSha256, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            var source = await ReadPluginSourceAsync(tx, cancellationToken)
                ?? throw new InvalidOperationException("The current project is not a plugin project.");
            if (!string.Equals(source.Info.Sha256, expectedSourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Plugin source changed. Reopen the plugin before exporting.");
            var translations = new Dictionary<string, string>(StringComparer.Ordinal);
            await using var query = _connection.CreateCommand();
            query.Transaction = tx;
            query.CommandText = "SELECT b.FieldKey,s.DestText,s.Status FROM PluginStringBinding b JOIN StringEntry s ON s.Id=b.StringId WHERE s.Status IN ($Done,$Edited);";
            query.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
            query.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var dest = reader.GetString(1);
                if ((StringEntryStatus)reader.GetInt32(2) == StringEntryStatus.Edited || !string.IsNullOrWhiteSpace(dest))
                    translations.Add(reader.GetString(0), dest);
            }
            return translations;
        }
        finally { _gate.Release(); }
    }
}
