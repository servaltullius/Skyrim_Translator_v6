using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.App.Services;

public sealed class GlobalTranslationMemoryService
{
    private readonly GlobalProjectDbService _globalDbService;

    public GlobalTranslationMemoryService(GlobalProjectDbService globalDbService)
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
        var pairs = TranslationMemoryFileService.ParseTsvPairs(await File.ReadAllLinesAsync(tsvPath, cancellationToken));
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
            throw new InvalidOperationException("Global DB is not available.");
        }

        return db;
    }
}

