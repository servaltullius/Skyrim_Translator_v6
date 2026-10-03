using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public sealed class GlobalProjectDbService
{
    private readonly BuiltInGlossaryService _builtInGlossaryService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<BethesdaFranchise, ProjectDb> _dbByFranchise = new();

    public GlobalProjectDbService(BuiltInGlossaryService builtInGlossaryService)
    {
        _builtInGlossaryService = builtInGlossaryService;
    }

    public BethesdaFranchise SelectedFranchise { get; set; } = BethesdaFranchise.ElderScrolls;

    public ProjectDb? Current
        => _dbByFranchise.TryGetValue(SelectedFranchise, out var db) ? db : null;

    public async Task<ProjectDb?> GetOrCreateAsync(CancellationToken cancellationToken)
        => await GetOrCreateAsync(SelectedFranchise, cancellationToken);

    public async Task<ProjectDb?> GetOrCreateAsync(BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        if (_dbByFranchise.TryGetValue(franchise, out var existing))
        {
            return existing;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_dbByFranchise.TryGetValue(franchise, out existing))
            {
                return existing;
            }

            ProjectDb? db = null;
            try
            {
                var dbPath = ProjectPaths.GetGlobalGlossaryDbPath(franchise);
                var shouldInsertMissingBuiltInEntries = !File.Exists(dbPath);
                db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
                await _builtInGlossaryService.EnsureBuiltInGlossaryAsync(
                    db,
                    cancellationToken,
                    insertMissingEntries: shouldInsertMissingBuiltInEntries,
                    franchise: franchise
                );
                await AddLaterBuiltInEntriesAsync(db, dbPath, franchise, cancellationToken);
                _dbByFranchise[franchise] = db;
                return db;
            }
            catch
            {
                if (db != null)
                {
                    await db.DisposeAsync();
                }

                return null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task AddLaterBuiltInEntriesAsync(ProjectDb db, string dbPath, BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        // Optional: a failure here must not make the whole global glossary unavailable.
        try
        {
            foreach (var batch in BuiltInGlossaryService.LaterAdditions)
            {
                var stampPath = ProjectPaths.GetBuiltInGlossaryAdditionsStampPath(dbPath, batch.Version);
                await _builtInGlossaryService.AddLaterEntriesOnceAsync(db, stampPath, batch.Version, franchise, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"WARN 기본 용어집 추가 항목을 반영하지 못했습니다: {ex}");
        }
    }
}
