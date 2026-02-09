using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public readonly record struct TranslationMemoryEntry(long Id, string SourceText, string DestText, string UpdatedAt);

public sealed partial class ProjectDb
{
    public async Task<IReadOnlyList<TranslationMemoryEntry>> GetTranslationMemoryEntriesAsync(
        string sourceLang,
        string destLang,
        CancellationToken cancellationToken
    )
    {
        var sourceLangKey = TranslationMemoryKey.NormalizeLanguage(sourceLang);
        var destLangKey = TranslationMemoryKey.NormalizeLanguage(destLang);

        if (string.IsNullOrWhiteSpace(sourceLangKey) || string.IsNullOrWhiteSpace(destLangKey))
        {
            return Array.Empty<TranslationMemoryEntry>();
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var list = new List<TranslationMemoryEntry>();
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT Id, SrcText, DstText, UpdatedAt
                FROM TranslationMemory
                WHERE SourceLangKey=$SourceLangKey AND DestLangKey=$DestLangKey
                ORDER BY UpdatedAt DESC, Id DESC;
                """;
            cmd.Parameters.AddWithValue("$SourceLangKey", sourceLangKey);
            cmd.Parameters.AddWithValue("$DestLangKey", destLangKey);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(
                    new TranslationMemoryEntry(
                        Id: reader.GetInt64(0),
                        SourceText: reader.GetString(1),
                        DestText: reader.GetString(2),
                        UpdatedAt: reader.GetString(3)
                    )
                );
            }

            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> BulkUpdateTranslationMemoryAsync(
        string sourceLang,
        string destLang,
        IReadOnlyList<(long Id, string DestText)> updates,
        CancellationToken cancellationToken
    )
    {
        if (updates.Count == 0)
        {
            return 0;
        }

        var sourceLangKey = TranslationMemoryKey.NormalizeLanguage(sourceLang);
        var destLangKey = TranslationMemoryKey.NormalizeLanguage(destLang);
        if (string.IsNullOrWhiteSpace(sourceLangKey) || string.IsNullOrWhiteSpace(destLangKey))
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var tx = _connection.BeginTransaction();
            await using var cmd = _connection.CreateCommand();
            var parameters = ConfigureBulkUpdateCommand(cmd, tx, sourceLangKey, destLangKey);

            var now = DateTimeOffset.UtcNow.ToString("O");
            var applied = 0;

            foreach (var (id, dstTextRaw) in updates)
            {
                if (!TryNormalizeBulkUpdate(id, dstTextRaw, out var normalizedId, out var normalizedDstText))
                {
                    continue;
                }

                parameters.Id.Value = normalizedId;
                parameters.DstText.Value = normalizedDstText;
                parameters.UpdatedAt.Value = now;

                var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
                if (rows > 0)
                {
                    applied += rows;
                }
            }

            tx.Commit();
            return applied;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static BulkUpdateCommandParameters ConfigureBulkUpdateCommand(
        DbCommand cmd,
        DbTransaction tx,
        string sourceLangKey,
        string destLangKey
    )
    {
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE TranslationMemory
            SET DstText=$DstText,
                UpdatedAt=$UpdatedAt
            WHERE Id=$Id
              AND SourceLangKey=$SourceLangKey
              AND DestLangKey=$DestLangKey
            ;
            """;

        var pId = cmd.CreateParameter();
        pId.ParameterName = "$Id";
        cmd.Parameters.Add(pId);

        var pSource = cmd.CreateParameter();
        pSource.ParameterName = "$SourceLangKey";
        pSource.Value = sourceLangKey;
        cmd.Parameters.Add(pSource);

        var pDest = cmd.CreateParameter();
        pDest.ParameterName = "$DestLangKey";
        pDest.Value = destLangKey;
        cmd.Parameters.Add(pDest);

        var pDstText = cmd.CreateParameter();
        pDstText.ParameterName = "$DstText";
        cmd.Parameters.Add(pDstText);

        var pUpdatedAt = cmd.CreateParameter();
        pUpdatedAt.ParameterName = "$UpdatedAt";
        cmd.Parameters.Add(pUpdatedAt);

        return new BulkUpdateCommandParameters(pId, pDstText, pUpdatedAt);
    }

    private static bool TryNormalizeBulkUpdate(
        long id,
        string? dstTextRaw,
        out long normalizedId,
        out string normalizedDstText
    )
    {
        normalizedId = id;
        normalizedDstText = (dstTextRaw ?? "").Trim();
        return normalizedId > 0 && !string.IsNullOrWhiteSpace(normalizedDstText);
    }

    private readonly record struct BulkUpdateCommandParameters(
        DbParameter Id,
        DbParameter DstText,
        DbParameter UpdatedAt
    );

    public async Task<int> DeleteTranslationMemoryAsync(
        string sourceLang,
        string destLang,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken
    )
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        var sourceLangKey = TranslationMemoryKey.NormalizeLanguage(sourceLang);
        var destLangKey = TranslationMemoryKey.NormalizeLanguage(destLang);
        if (string.IsNullOrWhiteSpace(sourceLangKey) || string.IsNullOrWhiteSpace(destLangKey))
        {
            return 0;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var tx = _connection.BeginTransaction();
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;

            var placeholders = new List<string>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                var name = $"$id{i}";
                placeholders.Add(name);
                cmd.Parameters.AddWithValue(name, ids[i]);
            }

            cmd.CommandText =
                $"""
                DELETE FROM TranslationMemory
                WHERE SourceLangKey=$SourceLangKey
                  AND DestLangKey=$DestLangKey
                  AND Id IN ({string.Join(",", placeholders)})
                ;
                """;

            cmd.Parameters.AddWithValue("$SourceLangKey", sourceLangKey);
            cmd.Parameters.AddWithValue("$DestLangKey", destLangKey);

            var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
            tx.Commit();
            return rows;
        }
        finally
        {
            _gate.Release();
        }
    }
}
