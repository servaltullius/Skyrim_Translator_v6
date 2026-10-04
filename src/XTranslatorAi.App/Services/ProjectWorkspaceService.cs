using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.App.Services;

public sealed partial class ProjectWorkspaceService
{
    private readonly GlobalProjectDbService _globalProjectDbService;
    private readonly BuiltInGlossaryService _builtInGlossaryService;
    private readonly string? _projectsRootOverride;

    public ProjectWorkspaceService(GlobalProjectDbService globalProjectDbService, BuiltInGlossaryService builtInGlossaryService,
        string? projectsRootOverride = null)
    {
        _globalProjectDbService = globalProjectDbService;
        _builtInGlossaryService = builtInGlossaryService;
        _projectsRootOverride = projectsRootOverride;
    }

    public sealed record LoadFromXmlRequest(
        string XmlPath,
        BethesdaFranchise SelectedFranchise,
        string SelectedModel,
        string CustomPromptText,
        bool UseCustomPrompt
    );

    public sealed record LoadFromXmlResult(
        ProjectDb Db,
        XTranslatorXmlInfo XmlInfo,
        string InputXmlPath,
        string SourceLang,
        string TargetLang,
        BethesdaFranchise Franchise,
        int RetainedTranslationCount = 0
    );

    /// @critical: Load XML → Project DB import.
    public async Task<LoadFromXmlResult> LoadFromXmlAsync(LoadFromXmlRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.XmlPath))
        {
            throw new ArgumentException("XML path is required.", nameof(request));
        }

        var xmlPath = request.XmlPath;
        var preInfo = await XTranslatorXmlImporter.ReadInfoAsync(xmlPath, cancellationToken);

        var (dbPath, franchise) = TryDetectFranchiseFromAddonName(preInfo.AddonName) is { } official
            ? (ProjectPaths.GetProjectDbPath(official, preInfo.AddonName, preInfo.SourceLang, preInfo.DestLang,
                _projectsRootOverride, xmlPath), official)
            : await FindXmlProjectAsync(request.SelectedFranchise, preInfo, xmlPath, cancellationToken);
        var legacyAddonPath = ProjectPaths.GetProjectDbPath(preInfo.AddonName, preInfo.SourceLang,
            preInfo.DestLang, _projectsRootOverride);
        var legacyInputPath = ProjectPaths.GetLegacyProjectDbPath(xmlPath, _projectsRootOverride);
        foreach (var legacyPath in new[] { legacyAddonPath, legacyInputPath })
        {
            if (File.Exists(dbPath)) break;
            await TryMigrateLegacyProjectDbAsync(legacyPath, dbPath, franchise, preInfo, xmlPath, cancellationToken);
        }

        var db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);
        try
        {
            if (await db.TryGetPluginSourceAsync(cancellationToken) != null)
                throw new InvalidOperationException("Cannot import XML into a plugin project.");
            var info = await XTranslatorXmlImporter.ImportToDbAsync(db, xmlPath, cancellationToken,
                preserveExistingTranslations: true,
                projectFactory: xml => CreateProjectInfo(xml, xmlPath, franchise, request.SelectedModel,
                    request.CustomPromptText, request.UseCustomPrompt));

            // Seed only after the complete input has been accepted; parse failures leave the old DB intact.
            var globalDb = await _globalProjectDbService.GetOrCreateAsync(franchise, cancellationToken);
            await _builtInGlossaryService.EnsureBuiltInGlossaryAsync(db, cancellationToken,
                insertMissingEntries: globalDb == null, franchise: franchise, applyMigrations: globalDb == null);

            // Translations of rows this file lacks (e.g. a partial export with the same Addon) are kept, not deleted.
            var retained = await db.GetRetiredTranslationCountAsync(cancellationToken);
            return new LoadFromXmlResult(db, info, xmlPath, info.SourceLang, info.DestLang, franchise, retained);
        }
        catch
        {
            // The caller takes ownership only after a successful return.
            await db.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// An XML project's folder is the game series selected when it was first opened, but the selection starts as
    /// Elder Scrolls on every launch and is not saved. A Fallout XML translated on day 1 under fallout\ opened on
    /// day 2 into a new, empty elder-scrolls\ project with the Skyrim glossary, TM and prompt. Switching the series
    /// with a project open also leaves its DB in the old folder and only records the new game inside it, so the
    /// next open reset that game to the selection. So every game folder is searched for this XML's project first,
    /// and the game stored in the project wins over the selection (the most recently opened one if there are
    /// several). Only an XML without a project anywhere gets a new one, under the selected game.
    /// </summary>
    private async Task<(string DbPath, BethesdaFranchise Franchise)> FindXmlProjectAsync(
        BethesdaFranchise selected, XTranslatorXmlInfo info, string xmlPath, CancellationToken cancellationToken)
    {
        (string Path, BethesdaFranchise Franchise, DateTimeOffset OpenedAt)? found = null;
        var franchises = new[] { selected }.Concat(Enum.GetValues<BethesdaFranchise>().Where(f => f != selected));
        foreach (var folderFranchise in franchises)
        {
            var path = ProjectPaths.GetProjectDbPath(folderFranchise, info.AddonName, info.SourceLang, info.DestLang,
                _projectsRootOverride, xmlPath);
            if (!File.Exists(path)) continue;
            var (stored, openedAt) = await TryReadStoredProjectGameAsync(path, cancellationToken);
            // Ties keep the earlier folder, which starts with the selected one.
            if (found == null || openedAt > found.Value.OpenedAt) found = (path, stored ?? folderFranchise, openedAt);
        }

        return found is { } existing
            ? (existing.Path, existing.Franchise)
            : (ProjectPaths.GetProjectDbPath(selected, info.AddonName, info.SourceLang, info.DestLang, _projectsRootOverride, xmlPath), selected);
    }

    /// <summary>The game recorded in a project DB and when it was last opened (each open rewrites the project row).</summary>
    private static async Task<(BethesdaFranchise? Franchise, DateTimeOffset OpenedAt)> TryReadStoredProjectGameAsync(
        string dbPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT Franchise, UpdatedAt FROM Project WHERE Id=1;";
            await using var row = await query.ExecuteReaderAsync(cancellationToken);
            if (!await row.ReadAsync(cancellationToken)) return (null, DateTimeOffset.MinValue);
            BethesdaFranchise? franchise = !row.IsDBNull(0) && Enum.TryParse<BethesdaFranchise>(row.GetString(0), true, out var parsed)
                ? parsed
                : null;
            var openedAt = !row.IsDBNull(1) && DateTimeOffset.TryParse(row.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var updated)
                ? updated
                : DateTimeOffset.MinValue;
            return (franchise, openedAt);
        }
        catch (SqliteException)
        {
            // Opening it for real reports the problem; a damaged DB must not send the XML to a new, empty project.
            return (null, DateTimeOffset.MinValue);
        }
    }

    public async Task ExportXmlAsync(ProjectDb db, XTranslatorXmlInfo xmlInfo, string outputPath, CancellationToken cancellationToken)
    {
        if (await db.TryGetPluginSourceAsync(cancellationToken) != null)
            throw new InvalidOperationException("Use plugin export for a plugin project.");
        await XTranslatorXmlExporter.ExportAsync(db, xmlInfo, outputPath, cancellationToken);
    }

    private static ProjectInfo CreateProjectInfo(
        XTranslatorXmlInfo info,
        string xmlPath,
        BethesdaFranchise franchise,
        string selectedModel,
        string customPromptText,
        bool useCustomPrompt
    )
    {
        var now = DateTimeOffset.UtcNow;
        var basePromptText = EmbeddedAssets.LoadMetaPrompt(franchise);
        return new ProjectInfo(
            Id: 1,
            InputXmlPath: xmlPath,
            AddonName: info.AddonName,
            Franchise: franchise,
            SourceLang: info.SourceLang,
            DestLang: info.DestLang,
            XmlVersion: info.Version,
            XmlHasBom: info.HasBom,
            XmlPrologLine: info.PrologLine,
            ModelName: selectedModel,
            BasePromptText: basePromptText,
            CustomPromptText: customPromptText,
            UseCustomPrompt: useCustomPrompt,
            CreatedAt: now,
            UpdatedAt: now
        );
    }

    private static BethesdaFranchise? TryDetectFranchiseFromAddonName(string? addonName)
    {
        if (string.IsNullOrWhiteSpace(addonName))
        {
            return null;
        }

        var name = Path.GetFileName(addonName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // Best-effort only: xTranslator XML generally does not encode the game.
        // Use obvious official master names when available (helps with base game/DLC translations).
        if (IsElderScrollsOfficialMaster(name))
        {
            return BethesdaFranchise.ElderScrolls;
        }
        if (IsFalloutOfficialMaster(name))
        {
            return BethesdaFranchise.Fallout;
        }
        if (IsStarfieldOfficialMaster(name))
        {
            return BethesdaFranchise.Starfield;
        }

        return null;
    }

    private static bool IsElderScrollsOfficialMaster(string fileName)
        => fileName is not null
           && fileName.Trim().ToLowerInvariant() is
               "skyrim.esm"
               or "update.esm"
               or "dawnguard.esm"
               or "hearthfires.esm"
               or "dragonborn.esm"
               or "oblivion.esm"
               or "morrowind.esm";

    private static bool IsFalloutOfficialMaster(string fileName)
        => fileName is not null
           && fileName.Trim().ToLowerInvariant() is
               "fallout4.esm"
               or "dlcrobot.esm"
               or "dlccoast.esm"
               or "dlcnukaworld.esm"
               or "dlcworkshop01.esm"
               or "dlcworkshop02.esm"
               or "dlcworkshop03.esm";

    private static bool IsStarfieldOfficialMaster(string fileName)
        => fileName is not null
           && fileName.Trim().ToLowerInvariant() is "starfield.esm";

    public static async Task<bool> TryMigrateLegacyProjectDbAsync(
        string legacyDbPath, string newDbPath, BethesdaFranchise franchise,
        XTranslatorXmlInfo xmlInfo, string inputXmlPath, CancellationToken cancellationToken)
    {
        if (File.Exists(newDbPath) || !File.Exists(legacyDbPath)) return false;
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = legacyDbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        await source.OpenAsync(cancellationToken);
        var hasFranchise = false;
        await using (var schema = source.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(Project);";
            await using var columns = await schema.ExecuteReaderAsync(cancellationToken);
            while (await columns.ReadAsync(cancellationToken))
                hasFranchise |= string.Equals(columns.GetString(1), "Franchise", StringComparison.OrdinalIgnoreCase);
        }
        await using (var query = source.CreateCommand())
        {
            query.CommandText = "SELECT InputXmlPath, AddonName, SourceLang, DestLang, "
                + (hasFranchise ? "Franchise" : "NULL") + " FROM Project WHERE Id=1;";
            await using var row = await query.ExecuteReaderAsync(cancellationToken);
            if (!await row.ReadAsync(cancellationToken)) return false;
            if (!string.Equals(row.IsDBNull(1) ? "" : row.GetString(1), xmlInfo.AddonName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.GetString(2), xmlInfo.SourceLang, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.GetString(3), xmlInfo.DestLang, StringComparison.OrdinalIgnoreCase)) return false;
            if (!row.IsDBNull(4))
            {
                if (!Enum.TryParse<BethesdaFranchise>(row.GetString(4), true, out var oldFranchise)
                    || oldFranchise != franchise) return false;
            }
            else if (!string.Equals(Path.GetFullPath(row.GetString(0)), Path.GetFullPath(inputXmlPath), StringComparison.OrdinalIgnoreCase))
            {
                // A DB predating the franchise field is ambiguous unless it belongs to this exact input.
                return false;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(newDbPath))!);
        var temporary = newDbPath + "." + Guid.NewGuid().ToString("N") + ".migration";
        try
        {
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
            }.ToString()))
            {
                await destination.OpenAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                source.BackupDatabase(destination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, newDbPath, overwrite: false);
            return true;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
