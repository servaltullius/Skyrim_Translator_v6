using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    /// <summary>
    /// Marks a row "원문 오류 수정" or takes the mark off. A marked row is translated against its English on purpose,
    /// so its translation is not the project TM's translation of that English: marking removes the TM entry when it
    /// holds the row's translation, and edits of the row are not written to the TM while it is marked. The project
    /// TM wins over the series TM, so another row with the same English was otherwise given the deliberate fix.
    /// </summary>
    public async Task SetSourceFixAsync(long stringId, bool mark, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var langKeys = await TryGetProjectLangKeysUnsafeAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using (var note = _connection.CreateCommand())
            {
                note.Transaction = tx;
                note.CommandText = mark
                    ? "INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt) VALUES ($Id, $Kind, '원문 오류 수정', $UpdatedAt) "
                      + "ON CONFLICT(StringId, Kind) DO UPDATE SET Message=excluded.Message, UpdatedAt=excluded.UpdatedAt;"
                    : "DELETE FROM StringNote WHERE StringId=$Id AND Kind=$Kind;";
                note.Parameters.AddWithValue("$Id", stringId);
                note.Parameters.AddWithValue("$Kind", TranslationConstants.SourceFixNoteKind);
                note.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
                await note.ExecuteNonQueryAsync(cancellationToken);
            }

            if (mark)
            {
                await RemoveRowTranslationFromTmUnsafeAsync(tx, stringId, langKeys, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RemoveRowTranslationFromTmUnsafeAsync(
        SqliteTransaction tx, long stringId, (string Source, string Dest) langKeys, CancellationToken cancellationToken)
    {
        string source;
        string dest;
        await using (var row = _connection.CreateCommand())
        {
            row.Transaction = tx;
            row.CommandText = "SELECT SourceText, DestText FROM StringEntry WHERE Id=$Id;";
            row.Parameters.AddWithValue("$Id", stringId);
            await using var reader = await row.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            (source, dest) = (reader.GetString(0), reader.GetString(1));
        }

        var srcKey = TranslationMemoryKey.NormalizeSource(source);
        if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(srcKey))
        {
            return;
        }

        await using var delete = _connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM TranslationMemory WHERE SourceLangKey=$SourceLangKey AND DestLangKey=$DestLangKey AND SrcKey=$SrcKey AND DstText=$DstText;";
        delete.Parameters.AddWithValue("$SourceLangKey", langKeys.Source);
        delete.Parameters.AddWithValue("$DestLangKey", langKeys.Dest);
        delete.Parameters.AddWithValue("$SrcKey", srcKey);
        delete.Parameters.AddWithValue("$DstText", dest);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<HashSet<long>> GetSourceFixIdsUnsafeAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<long>();
        await using var query = _connection.CreateCommand();
        query.CommandText = "SELECT StringId FROM StringNote WHERE Kind=$Kind;";
        query.Parameters.AddWithValue("$Kind", TranslationConstants.SourceFixNoteKind);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }
}
