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
                        // A row without letters (" ") is finished by keeping its source; it is not an empty translation.
                        if (status == StringEntryStatus.Edited || !string.IsNullOrWhiteSpace(dest)
                            || (dest.Length > 0 && string.Equals(dest, reader.GetString(1), StringComparison.Ordinal)))
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

            // Notes (TM applied, TM fallback) are kept for rows whose translation is kept, by field key: reopening
            // the plugin used to drop them all, so the grid lost its TM marks and the quality check its TM notes.
            var notesByKey = oldSource == null
                ? new Dictionary<string, (string Source, List<(string Kind, string Message, string UpdatedAt)> Notes)>(StringComparer.Ordinal)
                : await ReadPluginStringNotesAsync(tx, cancellationToken);

            await using (var clear = _connection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM PluginStringBinding; DELETE FROM StringNote; DELETE FROM StringEntry;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            var retiredCount = await RetireChangedPluginTranslationsAsync(tx, saved, sourceByKey, cancellationToken);
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
                var keptTranslation = false;
                if (saved.TryGetValue(field.Key, out var previous)
                    && string.Equals(previous.Source, field.SourceText, StringComparison.Ordinal))
                {
                    dest = previous.Dest;
                    status = previous.Status;
                    keptTranslation = true;
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
                if (keptTranslation && notesByKey.TryGetValue(field.Key, out var notes) && string.Equals(notes.Source, field.SourceText, StringComparison.Ordinal))
                {
                    await RestorePluginStringNotesAsync(tx, id, notes.Notes, cancellationToken);
                }
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
            LastPluginImportRetiredCount = retiredCount;
            // Return the exact committed rows so callers can adopt without another fallible/cancelable DB read.
            return imported;
        }
        finally { _gate.Release(); }
    }

    private async Task<Dictionary<string, (string Source, List<(string Kind, string Message, string UpdatedAt)> Notes)>> ReadPluginStringNotesAsync(
        SqliteTransaction tx, CancellationToken ct)
    {
        var notes = new Dictionary<string, (string Source, List<(string Kind, string Message, string UpdatedAt)> Notes)>(StringComparer.Ordinal);
        await using var query = _connection.CreateCommand();
        query.Transaction = tx;
        query.CommandText = "SELECT b.FieldKey, s.SourceText, n.Kind, n.Message, n.UpdatedAt FROM StringNote n "
                            + "JOIN StringEntry s ON s.Id = n.StringId JOIN PluginStringBinding b ON b.StringId = n.StringId;";
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = reader.GetString(0);
            if (!notes.TryGetValue(key, out var entry))
            {
                entry = (reader.GetString(1), new List<(string Kind, string Message, string UpdatedAt)>());
                notes[key] = entry;
            }

            entry.Notes.Add((reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return notes;
    }

    private async Task RestorePluginStringNotesAsync(
        SqliteTransaction tx, long stringId, IReadOnlyList<(string Kind, string Message, string UpdatedAt)> notes, CancellationToken ct)
    {
        await using var insert = _connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = "INSERT OR REPLACE INTO StringNote (StringId, Kind, Message, UpdatedAt) VALUES ($id, $kind, $message, $at);";
        var id = insert.Parameters.AddWithValue("$id", stringId);
        var kind = insert.Parameters.Add("$kind", SqliteType.Text);
        var message = insert.Parameters.Add("$message", SqliteType.Text);
        var at = insert.Parameters.Add("$at", SqliteType.Text);
        foreach (var note in notes)
        {
            (kind.Value, message.Value, at.Value) = (note.Kind, note.Message, note.UpdatedAt);
            await insert.ExecuteNonQueryAsync(ct);
        }
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
    /// <summary>Sets aside the translations of rows whose source changed or that are gone; returns how many this import set aside.</summary>
    private async Task<int> RetireChangedPluginTranslationsAsync(
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
        var count = 0;
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
            count++;
        }

        return count;
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
    /// Translations the last successful <see cref="ReplaceImportedPluginStringsAsync"/> set aside (not the ones kept
    /// from earlier imports), counted inside its transaction so the caller needs no fallible read after the commit.
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
