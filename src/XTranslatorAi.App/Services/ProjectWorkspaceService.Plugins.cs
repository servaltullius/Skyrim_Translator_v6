using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.Services;

public sealed partial class ProjectWorkspaceService
{
    public sealed record LoadFromPluginRequest(string InputPath, PluginReadOptions Options,
        string TargetLanguage, string TargetEncoding, string SelectedModel,
        string CustomPromptText, bool UseCustomPrompt);

    /// <param name="MovedFromPath">The plugin's earlier location when its project was continued after a move.</param>
    /// <param name="InheritedGlossary">Glossary entries a new project took from other projects of the same mod, and from which plugins.</param>
    /// <param name="ContinuedAfterReadSettingsChange">The plugin's project was continued after the Strings folder or metadata encoding changed.</param>
    public sealed record LoadFromPluginResult(ProjectDb Db, PluginDocument Document,
        string SourceLanguage, string TargetLanguage, string TargetEncoding,
        IReadOnlyList<StringEntry> Entries, string ProjectContext, string? MovedFromPath = null,
        (int Count, IReadOnlyList<string> FromPlugins) InheritedGlossary = default, int RetiredTranslations = 0,
        string? ContinuedFromTargetEncoding = null, bool ContinuedAfterReadSettingsChange = false);

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
        var isNewProject = !File.Exists(dbPath);
        var continued = isNewProject ? TryContinueMovedProject(request, document, dbPath) : null;
        // The same plugin opened with another output encoding continues that project rather than being "moved".
        var movedFrom = continued is { EarlierTargetEncoding: null, SamePlugin: false } ? continued.Value.InputPath : null;
        var continuedFromEncoding = continued?.EarlierTargetEncoding;
        var continuedAfterSettings = continued is { SamePlugin: true, EarlierTargetEncoding: null };
        var db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var project = new ProjectInfo(1, "", Path.GetFileName(document.Info.InputPath), BethesdaFranchise.ElderScrolls,
                request.Options.SourceLanguage, request.TargetLanguage, "", false, "", request.SelectedModel,
                EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls), request.CustomPromptText,
                request.UseCustomPrompt, now, now);
            await OpenGlobalDbAsync(BethesdaFranchise.ElderScrolls, cancellationToken);
            var inherited = isNewProject && continued == null
                ? await InheritModFamilyGlossaryAsync(db, request.InputPath, dbPath, cancellationToken)
                : default;
            var context = await db.TryGetProjectContextAsync(cancellationToken);
            // Import commit is the ownership-transfer point. No fallible/cancelable work may follow it:
            // the old visible workspace can share this database when reopening the same plugin.
            var entries = await db.ReplaceImportedPluginStringsAsync(document.Info, document.Fields, project,
                request.TargetEncoding, cancellationToken);
            var retired = db.LastPluginImportRetiredCount;
            return new LoadFromPluginResult(db, document, request.Options.SourceLanguage,
                request.TargetLanguage, request.TargetEncoding, entries, context?.ContextText ?? "", movedFrom, inherited, retired,
                continuedFromEncoding, continuedAfterSettings);
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

    /// <summary>
    /// Projects are keyed on the plugin's full path, so moving the mod folder ("…\모드팩 관리\0-Elden Rim…" to
    /// "…\모드팩 관리\엘든림\0-Elden Rim…") opened a new, empty project: Elden Rim was translated again from
    /// scratch, without its 493 reviewed rows, project glossary or context. When the new path has no project, a
    /// project for the same file name and settings is continued if it holds the same file (SHA-256) or its file is
    /// no longer where it was. It is copied to the new path, so the earlier project stays as it was, and the import
    /// that follows restores its translations by field key. Returns the earlier plugin path, or null.
    /// The output encoding is part of the project key too, so following E457's advice (choose UTF-8 and reopen)
    /// opened an empty project; a project of the same plugin with another output encoding is continued the same
    /// way, and <c>EarlierTargetEncoding</c> names its encoding. The Strings folder and the metadata encoding are in the
    /// key too, so a project of the same plugin read with other such settings is continued as well (<c>SamePlugin</c>).
    /// </summary>
    private static (string InputPath, string? EarlierTargetEncoding, bool SamePlugin)? TryContinueMovedProject(LoadFromPluginRequest request, PluginDocument document, string newDbPath)
    {
        var directory = Path.GetDirectoryName(newDbPath);
        var stem = Path.GetFileNameWithoutExtension(newDbPath);
        var hashDot = stem.LastIndexOf('.');
        if (directory == null || hashDot <= 0 || !Directory.Exists(directory))
        {
            return null;
        }

        var candidates = new List<(string DbPath, string InputPath, bool SameFile, DateTime Written, string? Encoding, bool SamePlugin)>();
        foreach (var file in Directory.EnumerateFiles(directory, stem[..(hashDot + 1)] + "*.sqlite"))
        {
            var hash = Path.GetFileNameWithoutExtension(file)[(hashDot + 1)..];
            if (hash.Length != 32 || !hash.All(char.IsAsciiHexDigitLower)
                || string.Equals(file, newDbPath, StringComparison.OrdinalIgnoreCase)
                || TryReadPluginProject(file) is not { } earlier)
            {
                continue;
            }

            var info = earlier.Source;
            var samePath = string.Equals(Path.GetFullPath(info.InputPath), Path.GetFullPath(request.InputPath), StringComparison.OrdinalIgnoreCase);
            var otherEncoding = !string.Equals(earlier.TargetEncoding, request.TargetEncoding, StringComparison.OrdinalIgnoreCase);
            var sameOther = info.Options.Game == document.Info.Options.Game
                && string.Equals(info.Options.SourceLanguage, request.Options.SourceLanguage, StringComparison.OrdinalIgnoreCase)
                && string.Equals(info.Options.SourceEncoding, document.Info.Options.SourceEncoding, StringComparison.OrdinalIgnoreCase)
                && string.Equals(earlier.TargetLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase);
            if (samePath && sameOther)
            {
                candidates.Add((file, info.InputPath, true, File.GetLastWriteTimeUtc(file), otherEncoding ? earlier.TargetEncoding : null, true));
                continue;
            }

            var sameSettings = string.Equals(Path.GetFileName(info.InputPath), Path.GetFileName(request.InputPath), StringComparison.OrdinalIgnoreCase)
                && !samePath
                && info.Options.Game == document.Info.Options.Game
                && string.Equals(info.Options.SourceLanguage, request.Options.SourceLanguage, StringComparison.OrdinalIgnoreCase)
                && string.Equals(info.Options.SourceEncoding, document.Info.Options.SourceEncoding, StringComparison.OrdinalIgnoreCase)
                && string.Equals(earlier.TargetEncoding, request.TargetEncoding, StringComparison.OrdinalIgnoreCase)
                && string.Equals(earlier.TargetLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase);
            var sameFile = string.Equals(info.Sha256, document.Info.Sha256, StringComparison.OrdinalIgnoreCase);
            if (sameSettings && (sameFile || !File.Exists(info.InputPath)))
            {
                candidates.Add((file, info.InputPath, sameFile, File.GetLastWriteTimeUtc(file), null, false));
            }
        }

        // The same plugin under other settings first, then a moved copy of the same file, newest first.
        var chosen = candidates.OrderByDescending(c => c.SamePlugin).ThenByDescending(c => c.SameFile)
            .ThenByDescending(c => c.Written).FirstOrDefault();
        if (chosen.DbPath == null)
        {
            return null;
        }

        // The backup API also carries pages still in a -wal file, which a file copy would leave behind.
        using (var source = new SqliteConnection($"Data Source={chosen.DbPath};Mode=ReadOnly;Pooling=False"))
        using (var target = new SqliteConnection($"Data Source={newDbPath};Pooling=False"))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }

        return (chosen.InputPath, chosen.Encoding, chosen.SamePlugin);
    }

    /// <summary>
    /// A mod split into plugins in sibling folders (엘든림\0-Elden Rim-Base, 1-Elden Rim-Weapon Art, 2-Elden Rim War
    /// Ash Pack01, …) got an empty project glossary for every plugin. War Ash Pack 2 and 3 came back with 습득 for
    /// "Learning" and four renderings of "A skill beyond the reach of most." while the other files used 배우기 and
    /// 상식을 벗어난 기술입니다. A new project starts with the enabled entries of the projects of the same mod: plugins in
    /// the same folder, or in sibling folders whose names start with the same word ("Elden"). Entries the projects
    /// translate differently are left out.
    /// </summary>
    private static async Task<(int Count, IReadOnlyList<string> FromPlugins)> InheritModFamilyGlossaryAsync(
        ProjectDb db, string inputPath, string newDbPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(newDbPath);
        if (directory == null || !Directory.Exists(directory))
        {
            return default;
        }

        var bySource = new Dictionary<string, List<GlossaryRow>>(StringComparer.OrdinalIgnoreCase);
        var fromPlugins = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.sqlite"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(file, newDbPath, StringComparison.OrdinalIgnoreCase)
                || TryReadPluginProject(file) is not { } sibling
                || !IsSameModFamily(sibling.Source.InputPath, inputPath))
            {
                continue;
            }

            var entries = ReadEnabledGlossary(file);
            if (entries.Count == 0)
            {
                continue;
            }

            fromPlugins.Add(Path.GetFileName(sibling.Source.InputPath));
            foreach (var entry in entries)
            {
                var key = entry.Source.Trim();
                if (!bySource.TryGetValue(key, out var list))
                {
                    bySource[key] = list = new List<GlossaryRow>();
                }

                list.Add(entry);
            }
        }

        var rows = bySource.Values
            .Where(list => list.Select(e => e.Target).Distinct(StringComparer.Ordinal).Count() == 1)
            .Select(list => list[0])
            .Select(e => (e.Category, SourceTerm: e.Source, TargetTerm: e.Target, Enabled: true, e.Priority, e.MatchMode, e.ForceMode, e.Note))
            .ToList();
        if (rows.Count == 0)
        {
            return default;
        }

        await db.BulkInsertGlossaryAsync(rows, cancellationToken);
        return (rows.Count, fromPlugins.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private readonly record struct GlossaryRow(string? Category, string Source, string Target, int MatchMode, int ForceMode, int Priority, string? Note);

    private static readonly Regex FolderWord = new(@"[A-Za-z가-힣]{3,}", RegexOptions.CultureInvariant);

    // Words that start many unrelated mod folder names.
    private static readonly HashSet<string> GenericFolderWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "skyrim", "sse", "mod", "mods", "patch", "patches", "fix", "fixes", "pack", "addon", "add", "update",
        "version", "main", "core", "base", "new", "more", "better", "unofficial", "official", "plugin", "plugins",
    };

    private static bool IsSameModFamily(string earlierPluginPath, string pluginPath)
    {
        var earlierFolder = Path.GetDirectoryName(Path.GetFullPath(earlierPluginPath));
        var folder = Path.GetDirectoryName(Path.GetFullPath(pluginPath));
        if (earlierFolder == null || folder == null)
        {
            return false;
        }

        if (string.Equals(earlierFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            // The game's Data folder holds every installed mod, so sharing it says nothing about the mod.
            return !string.Equals(Path.GetFileName(folder), "Data", StringComparison.OrdinalIgnoreCase);
        }

        var earlierParent = Path.GetDirectoryName(earlierFolder);
        if (earlierParent == null || !string.Equals(earlierParent, Path.GetDirectoryName(folder), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var earlierWord = FirstFolderWord(Path.GetFileName(earlierFolder));
        return earlierWord != null
               && string.Equals(earlierWord, FirstFolderWord(Path.GetFileName(folder)), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstFolderWord(string folderName)
        => FolderWord.Matches(folderName).Select(m => m.Value).FirstOrDefault(word => !GenericFolderWords.Contains(word));

    private static List<GlossaryRow> ReadEnabledGlossary(string dbPath)
    {
        var entries = new List<GlossaryRow>();
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Category, SrcTerm, DstTerm, MatchMode, ForceMode, Priority, Note FROM Glossary WHERE Enabled = 1;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new GlossaryRow(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }
        catch (SqliteException)
        {
            entries.Clear();
        }

        return entries;
    }

    private static (PluginSourceInfo Source, string TargetEncoding, string TargetLanguage)? TryReadPluginProject(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT s.SourceInfoJson, s.TargetEncoding, p.DestLang FROM ProjectSource s JOIN Project p ON p.Id = s.ProjectId "
                                  + "WHERE s.ProjectId = 1 AND s.Kind = 'plugin';";
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var source = System.Text.Json.JsonSerializer.Deserialize<PluginSourceInfo>(reader.GetString(0));
            return source == null ? null : (source, reader.GetString(1), reader.GetString(2));
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException or IOException or InvalidOperationException)
        {
            return null;
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
