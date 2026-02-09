using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    public async Task<IReadOnlyDictionary<string, string>> GetTranslationMemoryAsync(
        string sourceLang,
        string destLang,
        CancellationToken cancellationToken
    )
    {
        var sourceLangKey = TranslationMemoryKey.NormalizeLanguage(sourceLang);
        var destLangKey = TranslationMemoryKey.NormalizeLanguage(destLang);

        if (string.IsNullOrWhiteSpace(sourceLangKey) || string.IsNullOrWhiteSpace(destLangKey))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText =
                """
                SELECT SrcKey, DstText
                FROM TranslationMemory
                WHERE SourceLangKey=$SourceLangKey AND DestLangKey=$DestLangKey
                ORDER BY UpdatedAt DESC, Id DESC;
                """;

            cmd.Parameters.AddWithValue("$SourceLangKey", sourceLangKey);
            cmd.Parameters.AddWithValue("$DestLangKey", destLangKey);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = reader.GetString(0);
                var text = reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(key) && !dict.ContainsKey(key))
                {
                    dict[key] = text ?? "";
                }
            }

            return dict;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> BulkUpsertTranslationMemoryAsync(
        string sourceLang,
        string destLang,
        IReadOnlyList<(string SourceText, string DestText)> pairs,
        CancellationToken cancellationToken
    )
    {
        if (pairs.Count == 0)
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
            var parameters = ConfigureBulkUpsertCommand(cmd, tx, sourceLangKey, destLangKey);

            var applied = 0;
            foreach (var (srcTextRaw, dstTextRaw) in pairs)
            {
                if (!TryNormalizeUpsertPair(srcTextRaw, dstTextRaw, out var srcKey, out var srcText, out var dstText))
                {
                    continue;
                }

                parameters.SrcKey.Value = srcKey;
                parameters.SrcText.Value = srcText;
                parameters.DstText.Value = dstText;
                parameters.UpdatedAt.Value = DateTimeOffset.UtcNow.ToString("O");

                await cmd.ExecuteNonQueryAsync(cancellationToken);
                applied++;
            }

            tx.Commit();
            return applied;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static BulkUpsertCommandParameters ConfigureBulkUpsertCommand(
        DbCommand cmd,
        DbTransaction tx,
        string sourceLangKey,
        string destLangKey
    )
    {
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO TranslationMemory (
              SourceLangKey,
              DestLangKey,
              SrcKey,
              SrcText,
              DstText,
              UpdatedAt
            ) VALUES (
              $SourceLangKey,
              $DestLangKey,
              $SrcKey,
              $SrcText,
              $DstText,
              $UpdatedAt
            )
            ON CONFLICT(SourceLangKey, DestLangKey, SrcKey) DO UPDATE SET
              SrcText=excluded.SrcText,
              DstText=excluded.DstText,
              UpdatedAt=excluded.UpdatedAt
            ;
            """;

        var pSource = cmd.CreateParameter();
        pSource.ParameterName = "$SourceLangKey";
        pSource.Value = sourceLangKey;
        cmd.Parameters.Add(pSource);

        var pDest = cmd.CreateParameter();
        pDest.ParameterName = "$DestLangKey";
        pDest.Value = destLangKey;
        cmd.Parameters.Add(pDest);

        var pSrcKey = cmd.CreateParameter();
        pSrcKey.ParameterName = "$SrcKey";
        cmd.Parameters.Add(pSrcKey);

        var pSrcText = cmd.CreateParameter();
        pSrcText.ParameterName = "$SrcText";
        cmd.Parameters.Add(pSrcText);

        var pDstText = cmd.CreateParameter();
        pDstText.ParameterName = "$DstText";
        cmd.Parameters.Add(pDstText);

        var pUpdatedAt = cmd.CreateParameter();
        pUpdatedAt.ParameterName = "$UpdatedAt";
        cmd.Parameters.Add(pUpdatedAt);

        return new BulkUpsertCommandParameters(pSrcKey, pSrcText, pDstText, pUpdatedAt);
    }

    private static bool TryNormalizeUpsertPair(
        string? srcTextRaw,
        string? dstTextRaw,
        out string srcKey,
        out string srcText,
        out string dstText
    )
    {
        srcKey = "";
        srcText = srcTextRaw ?? "";
        dstText = dstTextRaw ?? "";

        if (string.IsNullOrWhiteSpace(srcText) || string.IsNullOrWhiteSpace(dstText))
        {
            return false;
        }

        srcKey = TranslationMemoryKey.NormalizeSource(srcText);
        return !string.IsNullOrWhiteSpace(srcKey);
    }

    private readonly record struct BulkUpsertCommandParameters(
        DbParameter SrcKey,
        DbParameter SrcText,
        DbParameter DstText,
        DbParameter UpdatedAt
    );
}
