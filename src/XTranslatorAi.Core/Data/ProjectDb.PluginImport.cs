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
                    if (Convert.ToInt64(await query.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 0)
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

            var sourceByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                sourceByKey.TryAdd(field.Key, field.SourceText);
            }

            RejectTranslatedPluginAsSource(saved, sourceByKey);
            var retired = oldSource == null
                ? new Dictionary<(string Key, string Source), (string Dest, StringEntryStatus Status)>()
                : await ReadRetiredPluginTranslationsAsync(tx, cancellationToken);

            await using (var clear = _connection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM PluginStringBinding; DELETE FROM StringNote; DELETE FROM StringEntry;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await RetireChangedPluginTranslationsAsync(tx, saved, sourceByKey, cancellationToken);
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
                else if (retired.TryGetValue((field.Key, field.SourceText), out var kept))
                {
                    // The source is back to a text this row had before (the earlier plugin version reopened).
                    dest = kept.Dest;
                    status = kept.Status;
                    await DeleteRetiredPluginTranslationAsync(tx, field.Key, field.SourceText, cancellationToken);
                }
                parameters.BindRow((field.OrderIndex, null, null, null, field.EditorId, field.Rec,
                    field.SourceText, dest, status, ""), now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                keyParameter.Value = field.Key;
                fieldParameter.Value = JsonSerializer.Serialize(field);
                var id = Convert.ToInt64(await binding.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
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
            int retiredCount;
            await using (var count = _connection.CreateCommand())
            {
                count.Transaction = tx;
                count.CommandText = "SELECT COUNT(*) FROM PluginRetiredTranslation;";
                retiredCount = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await tx.CommitAsync(cancellationToken);
            LastPluginImportRetiredCount = retiredCount;
            // Return the exact committed rows so callers can adopt without another fallible/cancelable DB read.
            return imported;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The export writes the translated plugin with the same file and Strings names, so it can be opened again as
    /// the source by mistake. Every translated row then looks changed (its source is now its translation) and the
    /// whole project would be retired. Refused when most changed rows have their old translation as the new source.
    /// </summary>
    private static void RejectTranslatedPluginAsSource(
        IReadOnlyDictionary<string, (string Source, string Dest, StringEntryStatus Status)> saved,
        IReadOnlyDictionary<string, string> sourceByKey)
    {
        var changed = 0;
        var translatedAsSource = 0;
        foreach (var (key, previous) in saved)
        {
            if (!sourceByKey.TryGetValue(key, out var source) || string.Equals(source, previous.Source, StringComparison.Ordinal))
            {
                continue;
            }

            changed++;
            if (string.Equals(source, previous.Dest, StringComparison.Ordinal))
            {
                translatedAsSource++;
            }
        }

        if (translatedAsSource >= 3 && translatedAsSource * 2 >= changed)
        {
            throw new InvalidDataException($"번역된 플러그인을 원문으로 열 수 없습니다: {translatedAsSource}행");
        }
    }

    /// <summary>
    /// A row whose source changed (a mod update) or that the plugin no longer has used to lose its translation for
    /// good, reviewed ones included. It is kept aside by field key and source, and comes back when that source does.
    /// </summary>
    private async Task RetireChangedPluginTranslationsAsync(
        SqliteTransaction tx,
        IReadOnlyDictionary<string, (string Source, string Dest, StringEntryStatus Status)> saved,
        IReadOnlyDictionary<string, string> sourceByKey,
        CancellationToken ct)
    {
        await using var retire = _connection.CreateCommand();
        retire.Transaction = tx;
        retire.CommandText = """
            INSERT INTO PluginRetiredTranslation(FieldKey, SourceText, DestText, Status, RetiredAt)
            VALUES ($key, $source, $dest, $status, $at)
            ON CONFLICT(FieldKey, SourceText) DO UPDATE SET DestText=$dest, Status=$status, RetiredAt=$at;
            """;
        var key = retire.Parameters.Add("$key", SqliteType.Text);
        var source = retire.Parameters.Add("$source", SqliteType.Text);
        var dest = retire.Parameters.Add("$dest", SqliteType.Text);
        var status = retire.Parameters.Add("$status", SqliteType.Integer);
        retire.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        foreach (var (fieldKey, previous) in saved)
        {
            if (sourceByKey.TryGetValue(fieldKey, out var current) && string.Equals(current, previous.Source, StringComparison.Ordinal))
            {
                continue;
            }

            key.Value = fieldKey;
            source.Value = previous.Source;
            dest.Value = previous.Dest;
            status.Value = (int)previous.Status;
            await retire.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task<Dictionary<(string Key, string Source), (string Dest, StringEntryStatus Status)>> ReadRetiredPluginTranslationsAsync(
        SqliteTransaction tx, CancellationToken ct)
    {
        var retired = new Dictionary<(string Key, string Source), (string Dest, StringEntryStatus Status)>();
        await using var query = _connection.CreateCommand();
        query.Transaction = tx;
        query.CommandText = "SELECT FieldKey, SourceText, DestText, Status FROM PluginRetiredTranslation;";
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            retired[(reader.GetString(0), reader.GetString(1))] = (reader.GetString(2), (StringEntryStatus)reader.GetInt32(3));
        }

        return retired;
    }

    private async Task DeleteRetiredPluginTranslationAsync(SqliteTransaction tx, string fieldKey, string source, CancellationToken ct)
    {
        await using var delete = _connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM PluginRetiredTranslation WHERE FieldKey=$key AND SourceText=$source;";
        delete.Parameters.AddWithValue("$key", fieldKey);
        delete.Parameters.AddWithValue("$source", source);
        await delete.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Translations kept aside after the last successful <see cref="ReplaceImportedPluginStringsAsync"/>, counted inside
    /// its transaction so the caller needs no fallible read after the commit.
    /// </summary>
    public int LastPluginImportRetiredCount { get; private set; }

    /// <summary>Translations kept aside because their row's source changed or the row left the plugin.</summary>
    public async Task<int> GetRetiredPluginTranslationCountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM PluginRetiredTranslation;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
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
