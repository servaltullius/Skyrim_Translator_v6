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
    /// <param name="EarlierProjectLeftAt">The earlier project DB path when a moved project could not be retired because it was in use.</param>
    /// <param name="LaterWorkElsewherePath">Another missing plugin path whose sibling project DB for the same file was written later.</param>
    public sealed record LoadFromPluginResult(ProjectDb Db, PluginDocument Document,
        string SourceLanguage, string TargetLanguage, string TargetEncoding,
        IReadOnlyList<StringEntry> Entries, string ProjectContext, string? MovedFromPath = null,
        (int Count, IReadOnlyList<string> FromPlugins) InheritedGlossary = default, int RetiredTranslations = 0,
        string? ContinuedFromTargetEncoding = null, bool ContinuedAfterReadSettingsChange = false,
        string? EarlierProjectLeftAt = null, string? LaterWorkElsewherePath = null);

    /// <summary>Runs after a project DB is opened and before the import; lets tests fail an open there.</summary>
    internal Func<CancellationToken, Task>? BeforePluginImportForTests { get; set; }

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
        var fields = await ResolveMasterSpeakersAsync(document, cancellationToken);
        var dbPath = ResolvePluginProjectDbPath(request, document, out var primaryPath);
        // Only a project of a sibling source encoding is left at this path, while the plugin's own project went with
        // the file to another folder and was worked on there since: this path continues that one (below), not the
        // sibling. Until the plugin's own project left with the file, it stood first here. What it left behind (the
        // retired file) tells the two apart: with none, the sibling is the project in use here, and a newer one
        // elsewhere is only told of (it opened in the sibling's place without a word).
        if (!string.Equals(dbPath, primaryPath, StringComparison.OrdinalIgnoreCase)
            && File.Exists(primaryPath + RetiredDbSuffix)
            && FindLaterWorkElsewhere(request, document, dbPath) != null)
        {
            dbPath = primaryPath;
        }

        var isNewProject = !File.Exists(dbPath);
        ProjectDb? db = null;
        try
        {
            // Check sibling DBs before opening/importing updates this DB's write timestamp.
            var laterWorkElsewhere = !isNewProject ? FindLaterWorkElsewhere(request, document, dbPath) : null;
            var continued = isNewProject ? TryContinueMovedProject(request, document, dbPath) : null;
            // The same plugin opened with another output encoding continues that project rather than being "moved".
            var movedFrom = continued is { EarlierTargetEncoding: null, SamePlugin: false } ? continued.Value.InputPath : null;
            var continuedFromEncoding = continued?.EarlierTargetEncoding;
            var continuedAfterSettings = continued is { SamePlugin: true, EarlierTargetEncoding: null };
            db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var project = new ProjectInfo(1, "", Path.GetFileName(document.Info.InputPath), BethesdaFranchise.ElderScrolls,
                request.Options.SourceLanguage, request.TargetLanguage, "", false, "", request.SelectedModel,
                EmbeddedAssets.LoadMetaPrompt(BethesdaFranchise.ElderScrolls), request.CustomPromptText,
                request.UseCustomPrompt, now, now);
            await OpenGlobalDbAsync(BethesdaFranchise.ElderScrolls, cancellationToken);
            if (BeforePluginImportForTests != null)
            {
                await BeforePluginImportForTests(cancellationToken);
            }

            var inherited = isNewProject && continued == null
                ? await InheritModFamilyGlossaryAsync(db, request.InputPath, dbPath, cancellationToken)
                : default;
            var context = await db.TryGetProjectContextAsync(cancellationToken);
            // Import commit is the ownership-transfer point. No fallible/cancelable work may follow it:
            // the old visible workspace can share this database when reopening the same plugin.
            var entries = await db.ReplaceImportedPluginStringsAsync(document.Info, fields, project,
                request.TargetEncoding, cancellationToken);
            var retired = db.LastPluginImportRetiredCount;
            // Retire the old DB only after the new project commits, so a failed or canceled first open never hides it.
            var earlierLeftAt = continued?.RetireDbPath is { } retirePath && !TryRetireMovedProjectDb(retirePath)
                ? retirePath
                : null;
            return new LoadFromPluginResult(db, document, request.Options.SourceLanguage,
                request.TargetLanguage, request.TargetEncoding, entries, context?.ContextText ?? "", movedFrom, inherited, retired,
                continuedFromEncoding, continuedAfterSettings, earlierLeftAt, laterWorkElsewhere);
        }
        catch
        {
            if (db != null)
            {
                await db.DisposeAsync();
            }

            // A project created by this open and never imported into is removed, so the next open is a first open
            // again: a canceled or failed first open used to leave an empty DB that was then opened as the project,
            // and a moved plugin was no longer continued nor the mod's glossary inherited.
            if (isNewProject)
            {
                DeleteUnimportedProject(dbPath);
            }

            throw;
        }
    }

    /// <summary>
    /// The plugin's fields with the speakers only the masters know (Serana by her voice type, by Dawnguard's alias)
    /// named from the game dictionary's speaker index; as read when there is no dictionary or nothing to resolve.
    /// </summary>
    private async Task<IReadOnlyList<PluginField>> ResolveMasterSpeakersAsync(PluginDocument document, CancellationToken cancellationToken)
    {
        if (_gameStrings == null || !document.Fields.Any(field => field.SpeakerHint != null || field.ListenerHint != null))
        {
            return document.Fields;
        }

        var index = (await _gameStrings.TryLoadAsync(cancellationToken))?.Speakers;
        return index == null
            ? document.Fields
            : index.Resolve(document.Fields, document.Info.Masters, Path.GetFileName(document.Info.InputPath));
    }

    private static void DeleteUnimportedProject(string dbPath)
    {
        ProjectDb.ReleasePool(dbPath);
        foreach (var path in new[] { dbPath, dbPath + "-wal", dbPath + "-shm", dbPath + "-journal" })
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Project DBs are keyed on the source encoding, and the reader may read a UTF-8 setting as Windows-1252.
    /// A mod update can cross that line: Mod.esp v1 is pure ASCII, so its 5,000 translated rows live in the
    /// UTF-8 project; v2 adds one Windows-1252 apostrophe and is read as Windows-1252. Keying only on the
    /// encoding read would open a new, empty project and leave the translated one unreachable. So an existing
    /// project for the encoding read comes first (Serana's Windows-1252 project), then one for the setting,
    /// then the setting's fallback project, for an update that drops the only Windows-1252 text, then the project of
    /// a plugin read string by string (UTF-8 and Windows-1252 mixed). Only a plugin
    /// with none of them gets a new project, keyed on the encoding read. The import then records the encoding
    /// read in that project, and export converts from it.
    /// </summary>
    /// <summary>
    /// Whether an earlier project's source encoding is the one this plugin was read with, counting as one the
    /// encodings the UTF-8 setting reads with (UTF-8, its Windows-1252 fallback, the two mixed): a moved mod whose
    /// update turned mixed started an empty project.
    /// </summary>
    private static bool SameSourceEncoding(string earlier, string read, string setting)
    {
        if (string.Equals(earlier, read, StringComparison.OrdinalIgnoreCase)) return true;
        var fallback = PluginReader.GetSourceEncodingFallback(setting);
        if (fallback == null) return false;
        bool Read(string encoding) => string.Equals(encoding, setting, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(encoding, fallback, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(encoding, PluginReader.MixedSourceEncoding, StringComparison.OrdinalIgnoreCase);
        return Read(earlier) && Read(read);
    }

    /// <param name="primaryPath">The project path of the encoding the plugin was read with; the result is another
    /// path when only a project of a sibling source encoding exists.</param>
    private string ResolvePluginProjectDbPath(LoadFromPluginRequest request, PluginDocument document, out string primaryPath)
    {
        var read = document.Info.Options;
        var setting = request.Options.SourceEncoding;
        var fallback = PluginReader.GetSourceEncodingFallback(setting);
        // A mixed file read string by string has a project of its own: an update that leaves it all UTF-8 or all
        // Windows-1252 continues that one, and the other way round.
        var paths = new[] { read.SourceEncoding, setting, fallback, fallback == null ? null : PluginReader.MixedSourceEncoding }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OfType<string>()
            .Select(encoding => ProjectPaths.GetPluginProjectDbPath(request.InputPath, read with { SourceEncoding = encoding },
                request.TargetLanguage, request.TargetEncoding, _projectsRootOverride))
            .ToArray();
        primaryPath = paths[0];
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
    // Built, not interpolated: a plugin named "Mod;Fix.esp" gave a project path with ";", which split the connection
    // string and made opening every later plugin fail.
    private static string ConnectionString(string dbPath, bool readOnly)
        => new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

    /// <summary>
    /// When a project DB was last written: the later of the file and its -wal. An empty -wal is not a write: opening a
    /// new plugin reads every project DB of the folder, which leaves one beside each, and an old project then read as
    /// worked on after the one in use.
    /// </summary>
    internal static DateTime? DbLastWriteUtc(string dbPath)
    {
        DateTime? latest = null;
        foreach (var path in new[] { dbPath, dbPath + "-wal" })
        {
            var file = new FileInfo(path);
            if (!file.Exists || (path != dbPath && file.Length == 0)) continue;
            var time = file.LastWriteTimeUtc;
            if (latest == null || time > latest.Value) latest = time;
        }

        return latest;
    }

    /// <summary>
    /// Archives a moved plugin's earlier project DB as <c>&lt;dbPath&gt;.moved</c> so returning the plugin to its
    /// original path sees no stale DB and continues from the latest location. Returns false without renaming if any
    /// step is blocked by another connection or lock, so an old -wal is never left behind for a future DB at that path.
    /// </summary>
    // After ".sqlite", so no search for project DBs ("*.sqlite") reads a retired one as a project.
    private const string RetiredDbSuffix = ".moved";

    public static bool TryRetireMovedProjectDb(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            return false;
        }

        try
        {
            ProjectDb.ReleasePool(dbPath);
            var connString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString();
            using (var connection = new SqliteConnection(connString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandTimeout = 1;
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var reader = command.ExecuteReader();
                if (!reader.Read() || reader.GetInt32(0) != 0)
                {
                    return false;
                }
            }

            foreach (var sidecar in new[] { dbPath + "-wal", dbPath + "-shm" })
            {
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }

            File.Move(dbPath, dbPath + RetiredDbSuffix, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateSiblingProjectDbs(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        var stem = Path.GetFileNameWithoutExtension(dbPath);
        var hashDot = stem.LastIndexOf('.');
        if (directory == null || hashDot <= 0 || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, stem[..(hashDot + 1)] + "*.sqlite"))
        {
            var hash = Path.GetFileNameWithoutExtension(file)[(hashDot + 1)..];
            if (hash.Length == 32 && hash.All(char.IsAsciiHexDigitLower)
                && !string.Equals(file, dbPath, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static bool MatchesMovedProjectSettings(PluginSourceInfo info, string targetEncoding, string targetLanguage,
        LoadFromPluginRequest request, PluginDocument document)
        => string.Equals(Path.GetFileName(info.InputPath), Path.GetFileName(request.InputPath), StringComparison.OrdinalIgnoreCase)
           && info.Options.Game == document.Info.Options.Game
           && string.Equals(info.Options.SourceLanguage, request.Options.SourceLanguage, StringComparison.OrdinalIgnoreCase)
           && SameSourceEncoding(info.Options.SourceEncoding, document.Info.Options.SourceEncoding, request.Options.SourceEncoding)
           && string.Equals(targetEncoding, request.TargetEncoding, StringComparison.OrdinalIgnoreCase)
           && string.Equals(targetLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase);

    // Projects split before move-retirement existed (or when retirement was blocked) keep both DBs: opening the older
    // one notifies the user of the newer missing sibling without altering either DB.
    private static string? FindLaterWorkElsewhere(LoadFromPluginRequest request, PluginDocument document, string dbPath)
    {
        if (DbLastWriteUtc(dbPath) is not { } currentWritten)
        {
            return null;
        }

        string? laterPath = null;
        var newestWritten = currentWritten;
        var fullInputPath = Path.GetFullPath(request.InputPath);
        foreach (var file in EnumerateSiblingProjectDbs(dbPath))
        {
            if (DbLastWriteUtc(file) is not { } written || written <= newestWritten)
            {
                continue;
            }

            if (TryReadPluginProject(file) is not { } sibling)
            {
                continue;
            }

            var info = sibling.Source;
            var samePath = string.Equals(Path.GetFullPath(info.InputPath), fullInputPath, StringComparison.OrdinalIgnoreCase);
            if (!samePath
                && MatchesMovedProjectSettings(info, sibling.TargetEncoding, sibling.TargetLanguage, request, document)
                && string.Equals(info.Sha256, document.Info.Sha256, StringComparison.OrdinalIgnoreCase)
                && !File.Exists(info.InputPath))
            {
                laterPath = info.InputPath;
                newestWritten = written;
            }
        }

        return laterPath;
    }

    private static (string InputPath, string? EarlierTargetEncoding, bool SamePlugin, string? RetireDbPath)? TryContinueMovedProject(
        LoadFromPluginRequest request, PluginDocument document, string newDbPath)
    {
        var candidates = new List<(string DbPath, string InputPath, bool SameFile, DateTime Written, string? Encoding, bool SamePlugin, bool ShouldRetire)>();
        var fullInputPath = Path.GetFullPath(request.InputPath);
        foreach (var file in EnumerateSiblingProjectDbs(newDbPath))
        {
            var written = DbLastWriteUtc(file) ?? File.GetLastWriteTimeUtc(file);
            if (TryReadPluginProject(file) is not { } earlier)
            {
                continue;
            }

            var info = earlier.Source;
            var samePath = string.Equals(Path.GetFullPath(info.InputPath), fullInputPath, StringComparison.OrdinalIgnoreCase);
            var otherEncoding = !string.Equals(earlier.TargetEncoding, request.TargetEncoding, StringComparison.OrdinalIgnoreCase);
            var sameOther = info.Options.Game == document.Info.Options.Game
                && string.Equals(info.Options.SourceLanguage, request.Options.SourceLanguage, StringComparison.OrdinalIgnoreCase)
                && SameSourceEncoding(info.Options.SourceEncoding, document.Info.Options.SourceEncoding, request.Options.SourceEncoding)
                && string.Equals(earlier.TargetLanguage, request.TargetLanguage, StringComparison.OrdinalIgnoreCase);
            if (samePath && sameOther)
            {
                candidates.Add((file, info.InputPath, true, written, otherEncoding ? earlier.TargetEncoding : null, true, false));
                continue;
            }

            var sameSettings = !samePath
                && MatchesMovedProjectSettings(info, earlier.TargetEncoding, earlier.TargetLanguage, request, document);
            var sameFile = string.Equals(info.Sha256, document.Info.Sha256, StringComparison.OrdinalIgnoreCase);
            var earlierMissing = !File.Exists(info.InputPath);
            if (sameSettings && (sameFile || earlierMissing))
            {
                candidates.Add((file, info.InputPath, sameFile, written, null, false, sameFile && earlierMissing));
            }
        }

        // The plugin's own projects first, the newest of them: the one of this path under other settings, and the one
        // that went with the file to another folder (ShouldRetire). The other-settings one alone went first, and a
        // plugin moved back to a path that had one opened that older text, not what was done where it had been.
        // Then a copy of the same file, newest first.
        var chosen = candidates.OrderByDescending(c => c.SamePlugin || c.ShouldRetire)
            .ThenByDescending(c => c.SamePlugin || c.ShouldRetire || c.SameFile)
            .ThenByDescending(c => c.Written).FirstOrDefault();
        if (chosen.DbPath == null)
        {
            return null;
        }

        // The backup API also carries pages still in a -wal file, which a file copy would leave behind. It writes a
        // temporary file that is moved in once complete, so an interrupted copy never becomes the project.
        var partial = newDbPath + ".partial";
        try
        {
            // A truncated .partial left by an interrupted copy makes SQLite's backup API fail.
            DeleteUnimportedProject(partial);
            using (var source = new SqliteConnection(ConnectionString(chosen.DbPath, readOnly: true)))
            using (var target = new SqliteConnection(ConnectionString(partial, readOnly: false)))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }

            // Clear any stray sidecars at the destination so an old -wal cannot attach to the moved DB.
            ProjectDb.ReleasePool(newDbPath);
            foreach (var sidecar in new[] { newDbPath + "-wal", newDbPath + "-shm" })
            {
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }

            File.Move(partial, newDbPath);
        }
        finally
        {
            DeleteUnimportedProject(partial);
        }

        return (chosen.InputPath, chosen.Encoding, chosen.SamePlugin, chosen.ShouldRetire ? chosen.DbPath : null);
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
            using var connection = new SqliteConnection(ConnectionString(dbPath, readOnly: true));
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
            using var connection = new SqliteConnection(ConnectionString(dbPath, readOnly: true));
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
