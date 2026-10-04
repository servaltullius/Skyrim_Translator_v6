using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public sealed class GlobalProjectDbService : IAsyncDisposable
{
    private readonly BuiltInGlossaryService _builtInGlossaryService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<BethesdaFranchise, ProjectDb> _dbByFranchise = new();
    private readonly string? _globalRootOverride;
    private readonly ConcurrentDictionary<BethesdaFranchise, string> _openErrorByFranchise = new();

    /// <param name="globalRootOverride">Folder used instead of %LOCALAPPDATA%\XTranslatorAi\Global, so tests never open the user's global DB.</param>
    public GlobalProjectDbService(BuiltInGlossaryService builtInGlossaryService, string? globalRootOverride = null)
    {
        _builtInGlossaryService = builtInGlossaryService;
        _globalRootOverride = globalRootOverride;
    }

    public BethesdaFranchise SelectedFranchise { get; set; } = BethesdaFranchise.ElderScrolls;

    public ProjectDb? Current
        => _dbByFranchise.TryGetValue(SelectedFranchise, out var db) ? db : null;

    public async Task<ProjectDb?> GetOrCreateAsync(CancellationToken cancellationToken)
        => await GetOrCreateAsync(SelectedFranchise, cancellationToken);

    public bool IsOpen(BethesdaFranchise franchise) => _dbByFranchise.ContainsKey(franchise);

    /// <summary>Why the global DB of <paramref name="franchise"/> could not be opened the last time, or null.</summary>
    public string? GetLastOpenError(BethesdaFranchise franchise)
        => _openErrorByFranchise.TryGetValue(franchise, out var error) ? error : null;

    /// <summary>
    /// Returns null when the DB cannot be opened; the next call tries again. Every failure used to become a silent
    /// null, cancellation included, so a locked or damaged global DB meant translating without the global glossary,
    /// series TM and official names with nothing said. Failures are now logged and kept for
    /// <see cref="GetLastOpenError"/>, and cancellation propagates.
    /// </summary>
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
            var dbPath = "";
            try
            {
                dbPath = ProjectPaths.GetGlobalGlossaryDbPath(franchise, _globalRootOverride);
                var migrationStampPath = ProjectPaths.GetBuiltInGlossaryAdditionsStampPath(dbPath, BuiltInGlossaryService.MigrationStampVersion);
                var seedStampPath = ProjectPaths.GetBuiltInGlossarySeedStampPath(dbPath);
                db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
                // Seeding used to depend on the file not existing before this open, so a first open that failed or
                // was canceled after creating the file left an empty global glossary for good. A glossary is seeded
                // while it is empty and unstamped; the stamp keeps a glossary the user emptied on purpose empty.
                var insertBuiltInEntries = !File.Exists(seedStampPath)
                    && (await db.GetGlossaryAsync(cancellationToken)).Count == 0;
                await _builtInGlossaryService.EnsureBuiltInGlossaryAsync(
                    db,
                    cancellationToken,
                    insertMissingEntries: insertBuiltInEntries,
                    franchise: franchise,
                    applyMigrations: !File.Exists(migrationStampPath)
                );
                await WriteStampAsync(migrationStampPath, cancellationToken);
                await WriteStampAsync(seedStampPath, cancellationToken);
                await AddLaterBuiltInEntriesAsync(db, dbPath, franchise, cancellationToken);
                _dbByFranchise[franchise] = db;
                _openErrorByFranchise.TryRemove(franchise, out _);
                return db;
            }
            catch (OperationCanceledException)
            {
                if (db != null)
                {
                    await db.DisposeAsync();
                }

                throw;
            }
            catch (Exception ex)
            {
                if (db != null)
                {
                    await db.DisposeAsync();
                }

                _openErrorByFranchise[franchise] = ex.Message;
                AppLog.Write($"ERROR 전체 용어집·시리즈 TM DB를 열지 못했습니다({franchise}, {dbPath}): {ex}");
                return null;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var franchise in _dbByFranchise.Keys)
        {
            if (_dbByFranchise.TryRemove(franchise, out var db))
            {
                await db.DisposeAsync();
            }
        }
    }

    private static async Task WriteStampAsync(string stampPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(stampPath))
            {
                await File.WriteAllTextAsync(stampPath, $"applied={DateTimeOffset.UtcNow:O}{Environment.NewLine}", cancellationToken);
            }
        }
        catch (IOException ex)
        {
            AppLog.Write($"WARN 기본 용어집 보정 기록을 저장하지 못했습니다: {ex.Message}");
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
