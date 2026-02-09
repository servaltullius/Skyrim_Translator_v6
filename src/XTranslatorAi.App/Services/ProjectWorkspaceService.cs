using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.App.Services;

public sealed class ProjectWorkspaceService
{
    private readonly GlobalProjectDbService _globalProjectDbService;
    private readonly BuiltInGlossaryService _builtInGlossaryService;

    public ProjectWorkspaceService(GlobalProjectDbService globalProjectDbService, BuiltInGlossaryService builtInGlossaryService)
    {
        _globalProjectDbService = globalProjectDbService;
        _builtInGlossaryService = builtInGlossaryService;
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

        var legacyDbPath = ProjectPaths.GetLegacyProjectDbPath(xmlPath);
        var dbPath = string.IsNullOrWhiteSpace(preInfo.AddonName)
            ? legacyDbPath
            : ProjectPaths.GetProjectDbPath(preInfo.AddonName, preInfo.SourceLang, preInfo.DestLang);

        TryMigrateLegacyProjectDb(legacyDbPath, dbPath);

        var db = await ProjectDb.OpenOrCreateAsync(dbPath, cancellationToken);

        var existingProject = await db.TryGetProjectAsync(cancellationToken);

        var franchise = existingProject?.Franchise
                        ?? TryDetectFranchiseFromAddonName(preInfo.AddonName)
                        ?? request.SelectedFranchise;

        // Best-effort: global DB may fail to open (network drive permissions, etc). Project should still load.
        var globalDb = await _globalProjectDbService.GetOrCreateAsync(franchise, cancellationToken);

        await _builtInGlossaryService.EnsureBuiltInGlossaryAsync(
            db,
            cancellationToken,
            insertMissingEntries: globalDb == null,
            franchise: franchise
        );

        await db.ClearStringsAsync(cancellationToken);

        var info = await XTranslatorXmlImporter.ImportToDbAsync(db, xmlPath, cancellationToken, ignoreDestText: true);

        var project = CreateProjectInfo(
            info,
            xmlPath,
            franchise,
            selectedModel: request.SelectedModel,
            customPromptText: request.CustomPromptText,
            useCustomPrompt: request.UseCustomPrompt
        );
        await db.UpsertProjectAsync(project, cancellationToken);

        return new LoadFromXmlResult(
            Db: db,
            XmlInfo: info,
            InputXmlPath: xmlPath,
            SourceLang: info.SourceLang,
            TargetLang: info.DestLang,
            Franchise: franchise
        );
    }

    public Task ExportXmlAsync(ProjectDb db, XTranslatorXmlInfo xmlInfo, string outputPath, CancellationToken cancellationToken)
    {
        return XTranslatorXmlExporter.ExportAsync(db, xmlInfo, outputPath, cancellationToken);
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

    private static void TryMigrateLegacyProjectDb(string legacyDbPath, string newDbPath)
    {
        if (string.IsNullOrWhiteSpace(legacyDbPath)
            || string.IsNullOrWhiteSpace(newDbPath)
            || string.Equals(legacyDbPath, newDbPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Migrate only if the new DB doesn't exist yet.
        if (File.Exists(newDbPath) || !File.Exists(legacyDbPath))
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(newDbPath));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            TryMoveFile(legacyDbPath, newDbPath);
            TryMoveFile(legacyDbPath + "-wal", newDbPath + "-wal");
            TryMoveFile(legacyDbPath + "-shm", newDbPath + "-shm");
        }
        catch
        {
            // Best-effort migration.
        }
    }

    private static void TryMoveFile(string source, string destination)
    {
        try
        {
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Move(source, destination);
            }
        }
        catch
        {
            // ignore
        }
    }
}
