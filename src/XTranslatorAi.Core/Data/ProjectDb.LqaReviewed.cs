using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    /// <summary>
    /// Quality-check results marked "확인함", by row and result code, with the row's translation when each was marked
    /// (TranslationConstants.LqaReviewedNoteKindPrefix). Serana Dialogue Add-On's 16 warnings left as they are on
    /// purpose came back on every scan; a mark hides a result until the row's translation changes.
    /// </summary>
    public async Task<IReadOnlyDictionary<(long StringId, string Code), string>> GetLqaReviewedMarksAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var marks = new Dictionary<(long StringId, string Code), string>();
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT StringId, Kind, Message FROM StringNote WHERE substr(Kind, 1, length($Prefix)) = $Prefix;";
            cmd.Parameters.AddWithValue("$Prefix", TranslationConstants.LqaReviewedNoteKindPrefix);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var code = reader.GetString(1)[TranslationConstants.LqaReviewedNoteKindPrefix.Length..];
                if (code.Length > 0)
                {
                    marks[(reader.GetInt64(0), code)] = reader.GetString(2);
                }
            }

            return marks;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Marks results "확인함" with the row's translation as it is now, or takes the marks off. The translation is kept
    /// as it is (not trimmed): the mark holds only while the row's text is the same.
    /// </summary>
    public async Task SetLqaReviewedAsync(
        IReadOnlyCollection<(long StringId, string Code, string DestText)> results, bool reviewed, CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = reviewed
                ? "INSERT INTO StringNote (StringId, Kind, Message, UpdatedAt) VALUES ($Id, $Kind, $Dest, $UpdatedAt) "
                  + "ON CONFLICT(StringId, Kind) DO UPDATE SET Message=excluded.Message, UpdatedAt=excluded.UpdatedAt;"
                : "DELETE FROM StringNote WHERE StringId=$Id AND Kind=$Kind;";
            var id = cmd.Parameters.Add("$Id", SqliteType.Integer);
            var kind = cmd.Parameters.Add("$Kind", SqliteType.Text);
            var dest = reviewed ? cmd.Parameters.Add("$Dest", SqliteType.Text) : null;
            if (reviewed)
            {
                cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
            }

            foreach (var (stringId, code, destText) in results)
            {
                if (stringId <= 0 || string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                id.Value = stringId;
                kind.Value = TranslationConstants.LqaReviewedNoteKindPrefix + code.Trim();
                if (dest != null)
                {
                    dest.Value = destText ?? "";
                }

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
