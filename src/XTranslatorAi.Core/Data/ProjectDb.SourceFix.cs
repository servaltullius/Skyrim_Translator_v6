using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    /// <summary>
    /// Marks a row "원문 오류 수정" or takes the mark off. A marked row is translated against its English on purpose,
    /// so its translation is not the project TM's translation of that English: marking removes the TM entry when it
    /// holds the row's translation, and edits of the row are not written to the TM while it is marked. The project
    /// TM wins over the series TM, so another row with the same English was otherwise given the deliberate fix.
    /// Only a translated row is marked (완료, 직접 수정, or an XML's row that came with its translation): a 대기 or 오류
    /// row marked kept the mark when the model's translation was saved, and the check skipped a translation that
    /// follows the English. Marking again takes off <see cref="TranslationConstants.SourceFixRecheckNoteKind"/>.
    /// Returns whether the row is marked afterwards: false for a row not translated, whose mark from before this rule
    /// is taken off (the GUI showed it unmarked while the agent's check still left it out).
    /// </summary>
    public async Task<bool> SetSourceFixAsync(long stringId, bool mark, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var langKeys = await TryGetProjectLangKeysUnsafeAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            var marked = mark;
            await using (var note = _connection.CreateCommand())
            {
                note.Transaction = tx;
                note.CommandText = mark
                    ? "INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt) SELECT Id, $Kind, '원문 오류 수정', $UpdatedAt FROM StringEntry "
                      + "WHERE Id=$Id AND (Status IN ($Done, $Edited) OR Status=$Skipped AND DestText<>'' "
                      + "AND Id NOT IN (SELECT StringId FROM StringNote WHERE Kind=$Hold)) "
                      + "ON CONFLICT(StringId, Kind) DO UPDATE SET Message=excluded.Message, UpdatedAt=excluded.UpdatedAt;"
                    : "DELETE FROM StringNote WHERE StringId=$Id AND Kind=$Kind;";
                note.Parameters.AddWithValue("$Id", stringId);
                note.Parameters.AddWithValue("$Kind", TranslationConstants.SourceFixNoteKind);
                note.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
                note.Parameters.AddWithValue("$Done", (int)StringEntryStatus.Done);
                note.Parameters.AddWithValue("$Edited", (int)StringEntryStatus.Edited);
                note.Parameters.AddWithValue("$Skipped", (int)StringEntryStatus.Skipped);
                note.Parameters.AddWithValue("$Hold", TranslationConstants.AgentHoldNoteKind);
                if (await note.ExecuteNonQueryAsync(cancellationToken) == 0 && mark)
                {
                    marked = false;
                }
            }

            await using (var unmark = _connection.CreateCommand())
            {
                unmark.Transaction = tx;
                unmark.CommandText = marked
                    ? "DELETE FROM StringNote WHERE StringId=$Id AND Kind=$Recheck;"
                    : "DELETE FROM StringNote WHERE StringId=$Id AND Kind IN ($Kind, $Recheck);";
                unmark.Parameters.AddWithValue("$Id", stringId);
                unmark.Parameters.AddWithValue("$Kind", TranslationConstants.SourceFixNoteKind);
                unmark.Parameters.AddWithValue("$Recheck", TranslationConstants.SourceFixRecheckNoteKind);
                await unmark.ExecuteNonQueryAsync(cancellationToken);
            }

            if (marked)
            {
                await RemoveRowTranslationFromTmUnsafeAsync(tx, stringId, langKeys, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return marked;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The rows the quality check leaves out as "원문 오류 수정": marked, less those an XML import gave another translation since.</summary>
    public async Task<IReadOnlySet<long>> GetSourceFixIdsForCheckAsync(CancellationToken cancellationToken)
    {
        var marked = await GetStringNotesByKindAsync(TranslationConstants.SourceFixNoteKind, cancellationToken);
        var recheck = await GetStringNotesByKindAsync(TranslationConstants.SourceFixRecheckNoteKind, cancellationToken);
        return marked.Keys.Where(id => !recheck.ContainsKey(id)).ToHashSet();
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

        // An unmarked row with the same English and translation (the record the text was pasted from) still owns it.
        await using (var shared = _connection.CreateCommand())
        {
            shared.Transaction = tx;
            shared.CommandText = "SELECT 1 FROM StringEntry WHERE Id<>$Id AND SourceText=$Source AND DestText=$Dest "
                                 + "AND Id NOT IN (SELECT StringId FROM StringNote WHERE Kind=$Kind) LIMIT 1;";
            shared.Parameters.AddWithValue("$Id", stringId);
            shared.Parameters.AddWithValue("$Source", source);
            shared.Parameters.AddWithValue("$Dest", dest);
            shared.Parameters.AddWithValue("$Kind", TranslationConstants.SourceFixNoteKind);
            if (await shared.ExecuteScalarAsync(cancellationToken) != null)
            {
                return;
            }
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
