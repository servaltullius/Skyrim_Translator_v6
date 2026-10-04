using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var dbPath = ResolvePluginProjectDbPath(request, document);
        var db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var project = new ProjectInfo(1, "", Path.GetFileName(document.Info.InputPath), BethesdaFranchise.ElderScrolls,
                request.Options.SourceLanguage, request.TargetLanguage, "", false, "", request.SelectedModel,
                EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls), request.CustomPromptText,
                request.UseCustomPrompt, now, now);
            await OpenGlobalDbAsync(BethesdaFranchise.ElderScrolls, cancellationToken);
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

    /// <summary>
    /// Project DBs are keyed on the source encoding, and the reader may read a UTF-8 setting as Windows-1252.
    /// A mod update can cross that line: Mod.esp v1 is pure ASCII, so its 5,000 translated rows live in the
    /// UTF-8 project; v2 adds one Windows-1252 apostrophe and is read as Windows-1252. Keying only on the
    /// encoding read would open a new, empty project and leave the translated one unreachable. So an existing
    /// project for the encoding read comes first (Serana's Windows-1252 project), then one for the setting,
    /// then the setting's fallback project, for an update that drops the only Windows-1252 text. Only a plugin
    /// with none of them gets a new project, keyed on the encoding read. The import then records the encoding
    /// read in that project, and export converts from it.
    /// </summary>
    private string ResolvePluginProjectDbPath(LoadFromPluginRequest request, PluginDocument document)
    {
        var read = document.Info.Options;
        var setting = request.Options.SourceEncoding;
        var paths = new[] { read.SourceEncoding, setting, PluginReader.GetSourceEncodingFallback(setting) }
            .OfType<string>()
            .Select(encoding => ProjectPaths.GetPluginProjectDbPath(request.InputPath, read with { SourceEncoding = encoding },
                request.TargetLanguage, request.TargetEncoding, _projectsRootOverride))
            .ToArray();
        return paths.FirstOrDefault(File.Exists) ?? paths[0];
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
