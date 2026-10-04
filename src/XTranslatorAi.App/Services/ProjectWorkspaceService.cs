using System;
using System.IO;
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
        BethesdaFranchise Franchise
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

        var franchise = TryDetectFranchiseFromAddonName(preInfo.AddonName) ?? request.SelectedFranchise;
        var dbPath = ProjectPaths.GetProjectDbPath(franchise, preInfo.AddonName, preInfo.SourceLang,
            preInfo.DestLang, _projectsRootOverride, xmlPath);
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

            return new LoadFromXmlResult(db, info, xmlPath, info.SourceLang, info.DestLang, franchise);
        }
        catch
        {
            // The caller takes ownership only after a successful return.
            await db.DisposeAsync();
            throw;
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
