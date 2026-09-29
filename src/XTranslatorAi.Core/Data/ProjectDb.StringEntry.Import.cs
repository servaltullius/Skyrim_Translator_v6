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

            await using (var clear = _connection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM StringNote; DELETE FROM StringEntry;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var insert = CreateBulkInsertStringsCommand(_connection, tx, out var p);
            var now = DateTimeOffset.UtcNow.ToString("O");
            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destText = row.DestText;
                var status = row.Status;
                if (saved.Count > 0 && saved.TryGetValue(GetImportIdentity(row.RawStringXml), out var candidates))
                {
                    var old = candidates.Count == 1 ? candidates[0] : candidates.Find(r => r.OrderIndex == row.OrderIndex);
                    // Explicit manual edits, including an intentional empty translation, win.
                    // Otherwise an explicitly translated incoming XML wins over earlier generated text.
                    if (old != null && (old.Status == StringEntryStatus.Edited || row.Status == StringEntryStatus.Pending))
                    {
                        destText = old.DestText;
                        status = old.Status;
                    }
                }
                p.BindRow((row.OrderIndex, row.ListAttr, row.PartialAttr, row.AttributesJson,
                    row.Edid, row.Rec, row.SourceText, destText, status, row.RawStringXml), now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
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

    private async Task<Dictionary<string, List<SavedTranslation>>> ReadSavedTranslationsAsync(
        SqliteTransaction tx, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<SavedTranslation>>(StringComparer.Ordinal);
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT OrderIndex, DestText, Status, RawStringXml FROM StringEntry WHERE Status IN ($Done,$Edited,$Skipped);";
        cmd.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
        cmd.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
        cmd.Parameters.AddWithValue("$Skipped", (int)StringEntryStatus.Skipped);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = (StringEntryStatus)reader.GetInt32(2);
            var dest = reader.GetString(1);
            if (status != StringEntryStatus.Edited && string.IsNullOrWhiteSpace(dest)) continue;
            var key = GetImportIdentity(reader.GetString(3));
            if (!result.TryGetValue(key, out var entries)) result[key] = entries = new List<SavedTranslation>();
            entries.Add(new SavedTranslation(reader.GetInt32(0), dest, status));
        }
        return result;
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

    private sealed record SavedTranslation(int OrderIndex, string DestText, StringEntryStatus Status);
}
