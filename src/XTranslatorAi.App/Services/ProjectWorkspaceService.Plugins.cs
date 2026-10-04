using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.Services;

public sealed partial class ProjectWorkspaceService
{
    public sealed record LoadFromPluginRequest(string InputPath, PluginReadOptions Options,
        string TargetLanguage, string TargetEncoding, string SelectedModel,
        string CustomPromptText, bool UseCustomPrompt);

    public sealed record LoadFromPluginResult(ProjectDb Db, PluginDocument Document,
        string SourceLanguage, string TargetLanguage, string TargetEncoding,
        IReadOnlyList<StringEntry> Entries, string ProjectContext);

    public Task<LoadFromPluginResult> LoadFromPluginAsync(LoadFromPluginRequest request, CancellationToken cancellationToken)
        // SQLite's async methods execute synchronously. Keep the complete parse/import operation
        // off the caller's UI context, including work before the first incomplete I/O await.
        => Task.Run(() => LoadFromPluginCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<LoadFromPluginResult> LoadFromPluginCoreAsync(LoadFromPluginRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetEncoding);
        // Parse and validate the complete input before opening or replacing any project DB.
        var document = await PluginReader.ReadAsync(request.InputPath, request.Options, cancellationToken);
        // The reader may have switched a UTF-8 setting to Windows-1252; the project follows what was read.
        var dbPath = ProjectPaths.GetPluginProjectDbPath(request.InputPath, document.Info.Options,
            request.TargetLanguage, request.TargetEncoding, _projectsRootOverride);
        var db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var project = new ProjectInfo(1, "", Path.GetFileName(document.Info.InputPath), BethesdaFranchise.ElderScrolls,
                request.Options.SourceLanguage, request.TargetLanguage, "", false, "", request.SelectedModel,
                EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls), request.CustomPromptText,
                request.UseCustomPrompt, now, now);
            var globalDb = await _globalProjectDbService.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, cancellationToken);
            await _builtInGlossaryService.EnsureBuiltInGlossaryAsync(db, cancellationToken,
                insertMissingEntries: globalDb == null, franchise: BethesdaFranchise.ElderScrolls, applyMigrations: globalDb == null);
            var context = await db.TryGetProjectContextAsync(cancellationToken);
            // Import commit is the ownership-transfer point. No fallible/cancelable work may follow it:
            // the old visible workspace can share this database when reopening the same plugin.
            var entries = await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project,
                request.TargetEncoding, cancellationToken);
            return new LoadFromPluginResult(db, document, request.Options.SourceLanguage,
                request.TargetLanguage, request.TargetEncoding, entries, context?.ContextText ?? "");
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public Task<PluginExportResult> ExportPluginAsync(ProjectDb db, PluginDocument document,
        PluginExportOptions options, CancellationToken cancellationToken)
        // Snapshot queries, binary rewriting and verification all belong to the same worker operation.
        => Task.Run(() => ExportPluginCoreAsync(db, document, options, cancellationToken), cancellationToken);

    private static async Task<PluginExportResult> ExportPluginCoreAsync(ProjectDb db, PluginDocument document,
        PluginExportOptions options, CancellationToken cancellationToken)
    {
        var translations = await db.GetPluginTranslationsForExportAsync(document.Info.Sha256, cancellationToken);
        return await PluginWriter.ExportAsync(document, translations, options, cancellationToken);
    }
}
