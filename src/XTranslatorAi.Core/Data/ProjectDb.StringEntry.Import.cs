using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    /// <summary>
    /// An XML project is identified by its Addon name, so every xTranslator XML of the same plugin (a partial
    /// export, an older or newer dump) opens the same project, which is intended. The import replaces all rows,
    /// and a file without some rows used to delete their translations silently: opening a 200-row partial export
    /// of a 5,000-row project dropped 4,800 finished translations. Translations of rows the incoming file does not
    /// contain are now kept aside (<c>RetiredTranslation</c>) and come back, under the same rules as rows still
    /// present, when a later import contains those rows again.
    /// </summary>
    internal async Task ReplaceImportedStringsAsync(
        IAsyncEnumerable<XTranslatorXmlStringRow> rows,
        bool preserveExistingTranslations,
        Func<ProjectInfo>? projectFactory,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            var saved = preserveExistingTranslations
                ? await ReadSavedTranslationsAsync(tx, cancellationToken)
                : new Dictionary<string, List<SavedTranslation>>(StringComparer.Ordinal);
            var retired = preserveExistingTranslations
                ? await ReadRetiredTranslationsAsync(tx, cancellationToken)
                : new Dictionary<string, List<SavedTranslation>>(StringComparer.Ordinal);
            var present = new HashSet<string>(StringComparer.Ordinal);
            var notes = preserveExistingTranslations
                ? await ReadImportStringNotesAsync(tx, cancellationToken)
                : new Dictionary<(string, int), List<(string Kind, string Message, string UpdatedAt)>>();

            await using (var clear = _connection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM StringNote; DELETE FROM StringEntry;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var insert = CreateBulkInsertStringsCommand(_connection, tx, out var p);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var takenHolds = new HashSet<SavedTranslation>(ReferenceEqualityComparer.Instance);
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destText = row.DestText;
                var status = row.Status;
                SavedTranslation? kept = null;
                var identity = saved.Count > 0 || retired.Count > 0 || notes.Count > 0 ? GetImportIdentity(row.RawStringXml) : null;
                if (identity != null)
                {
                    present.Add(identity);
                }

                // A row still in the project keeps its current translation over one kept from an earlier file.
                if (identity != null && (saved.TryGetValue(identity, out var candidates) || retired.TryGetValue(identity, out candidates)))
                {
                    var old = PickKeptTranslation(candidates, row.OrderIndex, takenHolds);
                    // Explicit manual edits, including an intentional empty translation, win.
                    // Otherwise an explicitly translated incoming XML wins over earlier generated text.
                    if (old != null && (old.Status == StringEntryStatus.Edited || row.Status == StringEntryStatus.Pending))
                    {
                        destText = old.DestText;
                        status = old.Status;
                    }

                    kept = old;
                }
                p.BindRow((row.OrderIndex, row.ListAttr, row.PartialAttr, row.AttributesJson,
                    row.Edid, row.Rec, row.SourceText, destText, status, row.RawStringXml), now);
                await insert.ExecuteNonQueryAsync(cancellationToken);

                // Notes (TM applied, TM fallback) describe the translation, so a row that keeps it keeps them:
                // reopening used to delete them all. A hand edit's TM notes are stale (saving the edit leaves them), but
                // the marks the user put on it stay: "원문 오류 수정" and "확인함", which reopening dropped on edited rows.
                // A translation set aside by an earlier import brings back the notes kept with it.
                // Another translation coming in keeps the "원문 오류 수정" mark only, and the check reads the new text in
                // full until the user marks the row again (SourceFixRecheckNoteKind): both marks were dropped, and a
                // deliberate fix came back marked as an error. A "확인함" judged the old text and goes.
                if (kept != null && status != StringEntryStatus.Pending)
                {
                    var keptNotes = notes.GetValueOrDefault((identity!, kept.OrderIndex));
                    var restored = keptNotes?.Where(note => StringNotePolicy.KeptWith(note.Kind, kept.Status)).ToList()
                                   ?? kept.Marks?.ToList() ?? new List<(string Kind, string Message, string UpdatedAt)>();
                    if (!string.Equals(destText, kept.DestText, StringComparison.Ordinal))
                    {
                        restored = restored.Where(note => note.Kind == TranslationConstants.SourceFixNoteKind).ToList();
                        if (restored.Count > 0) restored.Add((TranslationConstants.SourceFixRecheckNoteKind, "XML에서 번역이 바뀜", now));
                    }

                    if (restored.Count > 0)
                    {
                        await RestorePluginStringNotesAsync(tx, await LastInsertedRowIdAsync(tx, cancellationToken), restored, cancellationToken);
                    }
                }
                // A row still to translate keeps a sent-back edit's note (KeptWith), as a reopened plugin does.
                else if (status == StringEntryStatus.Pending && identity != null
                         && notes.GetValueOrDefault((identity, row.OrderIndex))?.Where(note => StringNotePolicy.KeptWith(note.Kind, status)).ToList()
                             is { Count: > 0 } waiting)
                {
                    await RestorePluginStringNotesAsync(tx, await LastInsertedRowIdAsync(tx, cancellationToken), waiting, cancellationToken);
                }
            }

            if (preserveExistingTranslations)
            {
                await RetainMissingTranslationsAsync(tx, saved, retired, present, notes, now, cancellationToken);
            }

            if (projectFactory != null)
            {
                await using var project = _connection.CreateCommand();
                project.Transaction = tx;
                ConfigureUpsertProjectCommand(project, projectFactory(), DateTimeOffset.UtcNow);
                await project.ExecuteNonQueryAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The kept row an incoming row takes its translation from. Rows sharing an identity have the same source, so after
    /// a shift any of them will do (a manual edit first): matching by position alone dropped LotD's repeated objectives
    /// when a row was added. A held row is kept with no translation: a translation of the same line goes before it (in
    /// its place, a twin row's translation or hand edit was gone when the file came back with fewer of them), and with
    /// none it holds one row only, the one in its place first (a waiting twin was held with it on every reopening).
    /// </summary>
    private static SavedTranslation? PickKeptTranslation(List<SavedTranslation> candidates, int orderIndex, HashSet<SavedTranslation> takenHolds)
    {
        SavedTranslation? inPlace = null, edited = null, first = null;
        foreach (var candidate in candidates)
        {
            if (IsHoldWithoutTranslation(candidate)) continue;
            first ??= candidate;
            if (candidate.OrderIndex == orderIndex) inPlace ??= candidate;
            if (candidate.Status == StringEntryStatus.Edited) edited ??= candidate;
        }

        if (first != null) return inPlace ?? edited ?? first;

        var hold = candidates.Find(r => r.OrderIndex == orderIndex && !takenHolds.Contains(r))
                   ?? candidates.Find(r => !takenHolds.Contains(r));
        if (hold != null) takenHolds.Add(hold);
        return hold;
    }

    // Only a held row is kept as 건너뜀 with no text (ReadSavedTranslationsAsync).
    private static bool IsHoldWithoutTranslation(SavedTranslation kept)
        => kept.Status == StringEntryStatus.Skipped && string.IsNullOrWhiteSpace(kept.DestText);

    private async Task<Dictionary<string, List<SavedTranslation>>> ReadSavedTranslationsAsync(
        SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<SavedTranslation>>(StringComparer.Ordinal);
        var agentKinds = StringNotePolicy.KindsOf(StringNotePolicy.NoteGroup.AgentState);
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT s.OrderIndex, s.DestText, s.Status, s.RawStringXml, "
                          + $"EXISTS(SELECT 1 FROM StringNote n WHERE n.StringId=s.Id AND n.Kind IN ({string.Join(",", agentKinds.Select((_, i) => "$Agent" + i))})) "
                          + "FROM StringEntry s WHERE s.Status IN ($Done,$Edited,$Skipped);";
        cmd.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
        cmd.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
        cmd.Parameters.AddWithValue("$Skipped", (int)StringEntryStatus.Skipped);
        for (var i = 0; i < agentKinds.Count; i++) cmd.Parameters.AddWithValue("$Agent" + i, agentKinds[i]);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = (StringEntryStatus)reader.GetInt32(2);
            var dest = reader.GetString(1);
            var isAgentHeld = status == StringEntryStatus.Skipped && reader.GetBoolean(4);
            if (status != StringEntryStatus.Edited && !isAgentHeld && string.IsNullOrWhiteSpace(dest)) continue;
            var key = GetImportIdentity(reader.GetString(3));
            if (!result.TryGetValue(key, out var entries)) result[key] = entries = new List<SavedTranslation>();
            entries.Add(new SavedTranslation(reader.GetInt32(0), dest, status));
        }
        return result;
    }

    private async Task<Dictionary<(string Identity, int OrderIndex), List<(string Kind, string Message, string UpdatedAt)>>> ReadImportStringNotesAsync(
        SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var result = new Dictionary<(string, int), List<(string Kind, string Message, string UpdatedAt)>>();
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT s.RawStringXml, s.OrderIndex, n.Kind, n.Message, n.UpdatedAt FROM StringNote n JOIN StringEntry s ON s.Id = n.StringId;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (GetImportIdentity(reader.GetString(0)), reader.GetInt32(1));
            if (!result.TryGetValue(key, out var list)) result[key] = list = new List<(string, string, string)>();
            list.Add((reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }
        return result;
    }

    private async Task<long> LastInsertedRowIdAsync(SqliteTransaction tx, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<Dictionary<string, List<SavedTranslation>>> ReadRetiredTranslationsAsync(
        SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<SavedTranslation>>(StringComparer.Ordinal);
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ImportIdentity, OrderIndex, DestText, Status, Marks FROM RetiredTranslation;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(0);
            if (!result.TryGetValue(key, out var entries)) result[key] = entries = new List<SavedTranslation>();
            entries.Add(new SavedTranslation(reader.GetInt32(1), reader.GetString(2), (StringEntryStatus)reader.GetInt32(3), ReadKeptNotes(reader, 4)));
        }
        return result;
    }

    /// <summary>Keeps translations of rows the imported file lacks; rows it contains again leave the side table.</summary>
    private async Task RetainMissingTranslationsAsync(
        SqliteTransaction tx,
        IReadOnlyDictionary<string, List<SavedTranslation>> saved,
        IReadOnlyDictionary<string, List<SavedTranslation>> retired,
        IReadOnlySet<string> present,
        IReadOnlyDictionary<(string Identity, int OrderIndex), List<(string Kind, string Message, string UpdatedAt)>> notes,
        string now,
        CancellationToken cancellationToken)
    {
        await using (var restore = _connection.CreateCommand())
        {
            restore.Transaction = tx;
            restore.CommandText = "DELETE FROM RetiredTranslation WHERE ImportIdentity=$Identity;";
            var identity = restore.Parameters.Add("$Identity", SqliteType.Text);
            foreach (var key in retired.Keys.Where(present.Contains))
            {
                identity.Value = key;
                await restore.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using var retire = _connection.CreateCommand();
        retire.Transaction = tx;
        retire.CommandText =
            """
            INSERT INTO RetiredTranslation (ImportIdentity, OrderIndex, DestText, Status, RetiredAt, Marks)
            VALUES ($Identity, $OrderIndex, $DestText, $Status, $RetiredAt, $Marks)
            ON CONFLICT(ImportIdentity, OrderIndex) DO UPDATE SET
              DestText=excluded.DestText, Status=excluded.Status, RetiredAt=excluded.RetiredAt, Marks=excluded.Marks;
            """;
        var pIdentity = retire.Parameters.Add("$Identity", SqliteType.Text);
        var pOrder = retire.Parameters.Add("$OrderIndex", SqliteType.Integer);
        var pDest = retire.Parameters.Add("$DestText", SqliteType.Text);
        var pStatus = retire.Parameters.Add("$Status", SqliteType.Integer);
        var pMarks = retire.Parameters.Add("$Marks", SqliteType.Text);
        retire.Parameters.AddWithValue("$RetiredAt", now);
        foreach (var (key, entries) in saved)
        {
            if (present.Contains(key)) continue;
            foreach (var entry in entries)
            {
                pIdentity.Value = key;
                pOrder.Value = entry.OrderIndex;
                pDest.Value = entry.DestText;
                pStatus.Value = (int)entry.Status;
                pMarks.Value = (object?)SerializeKeptNotes(notes.GetValueOrDefault((key, entry.OrderIndex)), entry.Status) ?? DBNull.Value;
                await retire.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    /// <summary>Translations kept from rows that the last imported XML did not contain.</summary>
    public async Task<int> GetRetiredTranslationCountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM RetiredTranslation;";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string GetImportIdentity(string rawXml)
    {
        var element = XElement.Parse(rawXml);
        // REC attributes contain record IDs in xTranslator XML. Comparing just REC.Value would
        // incorrectly merge different records with identical source text.
        var attributes = string.Join("|", element.Attributes().Where(a => a.Name.LocalName != "Partial")
            .OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => a.ToString()));
        return string.Join("\0", attributes,
            element.Element("EDID")?.ToString(SaveOptions.DisableFormatting) ?? "",
            element.Element("REC")?.ToString(SaveOptions.DisableFormatting) ?? "",
            element.Element("Source")?.Value ?? "");
    }

    /// <param name="Marks">The notes kept with a retired translation (none for a row still in the project, whose notes
    /// are read from StringNote).</param>
    private sealed record SavedTranslation(int OrderIndex, string DestText, StringEntryStatus Status,
        IReadOnlyList<(string Kind, string Message, string UpdatedAt)>? Marks = null);
}
