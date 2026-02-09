using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Data;

public sealed partial class ProjectDb
{
    private async Task<(string SourceLangKey, string DestLangKey)> TryGetProjectLangKeysUnsafeAsync(
        CancellationToken cancellationToken
    )
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT SourceLang, DestLang FROM Project WHERE Id=1 LIMIT 1;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ("", "");
        }

        var source = TranslationMemoryKey.NormalizeLanguage(reader.GetString(0));
        var dest = TranslationMemoryKey.NormalizeLanguage(reader.GetString(1));
        return (source, dest);
    }

    private async Task<string> TryGetStringSourceTextUnsafeAsync(long id, CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT SourceText FROM StringEntry WHERE Id=$Id LIMIT 1;";
        cmd.Parameters.AddWithValue("$Id", id);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(result) ?? "";
    }

    private async Task<Dictionary<long, string>> GetStringSourceTextsUnsafeAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken
    )
    {
        var dict = new Dictionary<long, string>(capacity: ids.Count);
        if (ids.Count == 0)
        {
            return dict;
        }

        await using var cmd = _connection.CreateCommand();
        var placeholders = new List<string>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            var name = $"$id{i}";
            placeholders.Add(name);
            cmd.Parameters.AddWithValue(name, ids[i]);
        }

        cmd.CommandText =
            $"SELECT Id, SourceText FROM StringEntry WHERE Id IN ({string.Join(",", placeholders)});";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var sourceText = reader.GetString(1);
            dict[id] = sourceText;
        }

        return dict;
    }

    private readonly record struct TranslationMemoryUpsertRequest(
        string SourceLangKey,
        string DestLangKey,
        string SrcKey,
        string SrcText,
        string DstText
    );

    private async Task UpsertTranslationMemoryUnsafeAsync(
        TranslationMemoryUpsertRequest request,
        CancellationToken cancellationToken
    )
    {
        await using var cmd = _connection.CreateCommand();
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

        cmd.Parameters.AddWithValue("$SourceLangKey", request.SourceLangKey);
        cmd.Parameters.AddWithValue("$DestLangKey", request.DestLangKey);
        cmd.Parameters.AddWithValue("$SrcKey", request.SrcKey);
        cmd.Parameters.AddWithValue("$SrcText", request.SrcText);
        cmd.Parameters.AddWithValue("$DstText", request.DstText);
        cmd.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TryUpsertEditedTranslationMemoryUnsafeAsync(long id, string destText, CancellationToken cancellationToken)
    {
        var (sourceLangKey, destLangKey) = await TryGetProjectLangKeysUnsafeAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceLangKey) || string.IsNullOrWhiteSpace(destLangKey))
        {
            return;
        }

        var sourceText = await TryGetStringSourceTextUnsafeAsync(id, cancellationToken);
        var srcKey = TranslationMemoryKey.NormalizeSource(sourceText);
        if (string.IsNullOrWhiteSpace(srcKey))
        {
            return;
        }

        await UpsertTranslationMemoryUnsafeAsync(
            new TranslationMemoryUpsertRequest(
                SourceLangKey: sourceLangKey,
                DestLangKey: destLangKey,
                SrcKey: srcKey,
                SrcText: sourceText,
                DstText: destText
            ),
            cancellationToken
        );
    }

    private async Task UpsertTranslationMemoryForEditedRowsUnsafeAsync(
        IReadOnlyList<(long Id, string DestText)> editedRows,
        CancellationToken cancellationToken
    )
    {
        if (editedRows.Count == 0)
        {
            return;
        }

        var (sourceLangKey, destLangKey) = await TryGetProjectLangKeysUnsafeAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceLangKey) || string.IsNullOrWhiteSpace(destLangKey))
        {
            return;
        }

        var ids = new long[editedRows.Count];
        for (var i = 0; i < editedRows.Count; i++)
        {
            ids[i] = editedRows[i].Id;
        }

        var sourceTexts = await GetStringSourceTextsUnsafeAsync(ids, cancellationToken);
        foreach (var (id, destText) in editedRows)
        {
            if (!sourceTexts.TryGetValue(id, out var sourceText))
            {
                continue;
            }

            var srcKey = TranslationMemoryKey.NormalizeSource(sourceText);
            if (string.IsNullOrWhiteSpace(srcKey))
            {
                continue;
            }

            await UpsertTranslationMemoryUnsafeAsync(
                new TranslationMemoryUpsertRequest(
                    SourceLangKey: sourceLangKey,
                    DestLangKey: destLangKey,
                    SrcKey: srcKey,
                    SrcText: sourceText,
                    DstText: destText
                ),
                cancellationToken
            );
        }
    }
}
