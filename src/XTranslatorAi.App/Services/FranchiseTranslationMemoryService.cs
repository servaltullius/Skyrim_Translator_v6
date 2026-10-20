using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.App.Services;

public sealed class FranchiseTranslationMemoryService
{
    private readonly GlobalProjectDbService _globalDbService;

    public FranchiseTranslationMemoryService(GlobalProjectDbService globalDbService)
    {
        _globalDbService = globalDbService;
    }

    public async Task<ProjectDb?> TryGetDbAsync(CancellationToken cancellationToken)
        => await _globalDbService.GetOrCreateAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetDictionaryAsync(
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken
    )
    {
        var db = await _globalDbService.GetOrCreateAsync(cancellationToken);
        if (db == null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return await db.GetTranslationMemoryAsync(sourceLang, targetLang, cancellationToken);
    }

    public async Task<IReadOnlyList<TranslationMemoryEntry>> GetEntriesAsync(
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken
    )
    {
        var db = await _globalDbService.GetOrCreateAsync(cancellationToken);
        if (db == null)
        {
            return Array.Empty<TranslationMemoryEntry>();
        }

        return await db.GetTranslationMemoryEntriesAsync(sourceLang, targetLang, cancellationToken);
    }

    public async Task<int> BulkUpsertAsync(
        string sourceLang,
        string targetLang,
        IReadOnlyList<(string SourceText, string DestText)> pairs,
        CancellationToken cancellationToken
    )
    {
        var db = await RequireDbAsync(cancellationToken);
        return await db.BulkUpsertTranslationMemoryAsync(sourceLang, targetLang, pairs, cancellationToken);
    }

    public async Task<int> BulkUpdateAsync(
        string sourceLang,
        string targetLang,
        IReadOnlyList<(long Id, string DestText)> updates,
        CancellationToken cancellationToken
    )
    {
        var db = await RequireDbAsync(cancellationToken);
        return await db.BulkUpdateTranslationMemoryAsync(sourceLang, targetLang, updates, cancellationToken);
    }

    public async Task<int> DeleteAsync(
        string sourceLang,
        string targetLang,
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken
    )
    {
        var db = await RequireDbAsync(cancellationToken);
        return await db.DeleteTranslationMemoryAsync(sourceLang, targetLang, ids, cancellationToken);
    }

    public async Task<int> ImportFromTsvAsync(
        string sourceLang,
        string targetLang,
        string tsvPath,
        CancellationToken cancellationToken
    )
    {
        // Not File.ReadAllLinesAsync: an Excel TSV from Korean Windows is CP949 (see ImportTextFileReader).
        var lines = await ImportTextFileReader.ReadAllLinesAsync(tsvPath, cancellationToken);
        if (TranslationMemoryFileService.ParseTsvChanges(lines) is { } changes)
        {
            var db = await RequireDbAsync(cancellationToken);
            return await db.ApplyTranslationMemoryChangesAsync(sourceLang, targetLang, changes, cancellationToken);
        }

        List<(string SourceText, string DestText)> pairs;
        try
        {
            pairs = TranslationMemoryFileService.ParseTsvPairs(lines);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // An exported TSV re-saved by Excel loses its JSON quotes. The error stopped the automatic import of every
            // file after this one (the bundled seed's changes too) on each load; it is now this file's alone, which stays
            // in tm-import to be fixed.
            throw new ImportFileEncodingException(
                $"{Path.GetFileName(tsvPath)}의 {ex.Message}이 TM 내보내기 형식(JSON 문자열 칸)이 아니어서 이 파일은 가져오지 않았습니다. "
                + "엑셀로 다시 저장하면 따옴표가 빠집니다. 내보낸 원래 파일을 쓰거나, 머리줄을 'Source<탭>Target'으로 바꿔 두 칸 TSV로 저장하세요.", ex);
        }

        if (pairs.Count == 0)
        {
            return 0;
        }

        return await BulkUpsertAsync(sourceLang, targetLang, pairs, cancellationToken);
    }

    private async Task<ProjectDb> RequireDbAsync(CancellationToken cancellationToken)
    {
        var db = await _globalDbService.GetOrCreateAsync(cancellationToken);
        if (db == null)
        {
            throw new InvalidOperationException("시리즈 TM DB를 사용할 수 없습니다.");
        }

        return db;
    }
}
