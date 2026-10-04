using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.Services;

public static class ProjectPaths
{
    public static string GetPluginProjectDbPath(string inputPath, PluginReadOptions options,
        string targetLanguage, string targetEncoding, string? projectsRootOverride = null)
    {
        var identity = string.Join("\0", Path.GetFullPath(inputPath).ToLowerInvariant(), options.Game,
            options.SourceLanguage.Trim().ToLowerInvariant(), targetLanguage.Trim().ToLowerInvariant(),
            options.SourceEncoding.Trim().ToLowerInvariant(), targetEncoding.Trim().ToLowerInvariant(),
            options.MetadataEncoding.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(options.StringsDirectory) ? "" : Path.GetFullPath(options.StringsDirectory).ToLowerInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32].ToLowerInvariant();
        var directory = Path.Combine(projectsRootOverride ?? GetProjectsBaseDir(), "elder-scrolls", "plugin", options.Game.ToString());
        var name = SanitizeFileNameStem(Path.GetFileNameWithoutExtension(inputPath), "plugin");
        return Path.Combine(directory, $"{name}.{hash}.sqlite");
    }

    public static string GetGlobalRootDir()
        => GetGlobalRootDir(globalRootOverride: null);

    public static string GetGlobalRootDir(string? globalRootOverride)
    {
        var root = string.IsNullOrWhiteSpace(globalRootOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XTranslatorAi",
                "Global"
            )
            : Path.GetFullPath(globalRootOverride);
        Directory.CreateDirectory(root);
        return root;
    }

    // Legacy path, retained only for safe migration of existing installations.
    public static string GetProjectDbPath(string addonName, string sourceLang, string destLang, string? projectsRootOverride = null)
    {
        var baseDir = projectsRootOverride ?? GetProjectsBaseDir();
        Directory.CreateDirectory(baseDir);

        var addonStem = Path.GetFileNameWithoutExtension((addonName ?? "").Trim());
        var safeAddon = SanitizeFileNameStem(addonStem, fallback: "project");
        var safeSourceLang = SanitizeFileNameStem(sourceLang, fallback: "src");
        var safeDestLang = SanitizeFileNameStem(destLang, fallback: "dst");
        var hash = ShortHash($"{addonName}|{sourceLang}|{destLang}");

        return Path.Combine(baseDir, $"{safeAddon}.{safeSourceLang}-{safeDestLang}.{hash}.sqlite");
    }

    public static string GetProjectDbPath(
        BethesdaFranchise franchise, string addonName, string sourceLang, string destLang,
        string? projectsRootOverride = null, string? inputPathForUnnamedProject = null)
    {
        var franchiseDir = franchise switch
        {
            BethesdaFranchise.ElderScrolls => "elder-scrolls",
            BethesdaFranchise.Fallout => "fallout",
            BethesdaFranchise.Starfield => "starfield",
            _ => throw new ArgumentOutOfRangeException(nameof(franchise)),
        };
        var baseDir = Path.Combine(projectsRootOverride ?? GetProjectsBaseDir(), franchiseDir);
        var name = (addonName ?? "").Trim();
        // Without an Addon name, the source path is the only available project identity.
        var identity = name.Length == 0
            ? Path.GetFullPath(inputPathForUnnamedProject ?? throw new ArgumentException("An unnamed project requires its input path."))
            : name;
        var hash = ShortHash($"{identity.ToLowerInvariant()}|{sourceLang.Trim().ToLowerInvariant()}|{destLang.Trim().ToLowerInvariant()}");
        var stem = SanitizeFileNameStem(Path.GetFileNameWithoutExtension(name), "project");
        return Path.Combine(baseDir, $"{stem}.{SanitizeFileNameStem(sourceLang, "src")}-{SanitizeFileNameStem(destLang, "dst")}.{hash}.sqlite");
    }

    public static string GetLegacyProjectDbPath(string inputXmlPath, string? projectsRootOverride = null)
    {
        var baseDir = projectsRootOverride ?? GetProjectsBaseDir();
        Directory.CreateDirectory(baseDir);

        var fileName = Path.GetFileNameWithoutExtension(inputXmlPath);
        var hash = ShortHash(inputXmlPath);
        var safeName = string.IsNullOrWhiteSpace(fileName) ? "project" : fileName;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(c, '_');
        }

        return Path.Combine(baseDir, $"{safeName}.{hash}.sqlite");
    }

    public static string GetGlobalGlossaryDbPath()
        => GetGlobalGlossaryDbPath(BethesdaFranchise.ElderScrolls);

    public static string GetGlobalGlossaryDbPath(BethesdaFranchise franchise)
    {
        var baseDir = GetGlobalRootDir(globalRootOverride: null);

        // Keep the legacy on-disk filename for Elder Scrolls so existing installs remain compatible.
        // Conceptually this is franchise-scoped shared glossary/TM data; only the path layout varies
        // by franchise for Fallout and Starfield.
        if (franchise == BethesdaFranchise.ElderScrolls)
        {
            return Path.Combine(baseDir, "global-glossary.sqlite");
        }

        var franchiseDir = franchise switch
        {
            BethesdaFranchise.Fallout => "fallout",
            BethesdaFranchise.Starfield => "starfield",
            _ => "other",
        };

        var dir = Path.Combine(baseDir, franchiseDir);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "global-glossary.sqlite");
    }

    public static string GetGlobalGlossaryDbPath(BethesdaFranchise franchise, string? globalRootOverride)
    {
        var baseDir = GetGlobalRootDir(globalRootOverride);

        // Keep the legacy on-disk filename for Elder Scrolls so existing installs remain compatible.
        // Conceptually this is franchise-scoped shared glossary/TM data; only the path layout varies
        // by franchise for Fallout and Starfield.
        if (franchise == BethesdaFranchise.ElderScrolls)
        {
            return Path.Combine(baseDir, "global-glossary.sqlite");
        }

        var franchiseDir = franchise switch
        {
            BethesdaFranchise.Fallout => "fallout",
            BethesdaFranchise.Starfield => "starfield",
            _ => "other",
        };

        var dir = Path.Combine(baseDir, franchiseDir);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "global-glossary.sqlite");
    }

    public static string GetGlobalTranslationMemoryImportDir(BethesdaFranchise franchise)
    {
        var dbPath = GetGlobalGlossaryDbPath(franchise);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = GetProjectsBaseDir();
        }

        var dir = Path.Combine(baseDir, "tm-import");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetGlobalTranslationMemoryImportDir(BethesdaFranchise franchise, string? globalRootOverride)
    {
        var dbPath = GetGlobalGlossaryDbPath(franchise, globalRootOverride);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = GetProjectsBaseDir();
        }

        var dir = Path.Combine(baseDir, "tm-import");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Marks that the built-in glossary additions of <paramref name="version"/> were offered to this glossary.</summary>
    public static string GetBuiltInGlossaryAdditionsStampPath(string globalGlossaryDbPath, string version)
        => Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(globalGlossaryDbPath)) ?? GetProjectsBaseDir(),
            $".builtin-glossary-additions.{version}.stamp"
        );

    /// <summary>Marks that the built-in glossary was offered to this (then empty) global glossary.</summary>
    public static string GetBuiltInGlossarySeedStampPath(string globalGlossaryDbPath)
        => Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(globalGlossaryDbPath)) ?? GetProjectsBaseDir(),
            ".builtin-glossary-seeded.stamp"
        );

    public static string GetBundledFranchiseTmSeedStampPath(BethesdaFranchise franchise, string version)
        => GetBundledFranchiseTmSeedStampPath(franchise, version, globalRootOverride: null);

    public static string GetBundledFranchiseTmSeedStampPath(BethesdaFranchise franchise, string version, string? globalRootOverride)
        => Path.Combine(
            GetGlobalTranslationMemoryImportDir(franchise, globalRootOverride),
            $".bundled-seed.{franchise.ToString().ToLowerInvariant()}.{version}.stamp"
        );

    private static string GetProjectsBaseDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XTranslatorAi",
            "Projects"
        );

    private static string SanitizeFileNameStem(string? value, string fallback)
    {
        var safe = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = fallback;
        }

        safe = safe.Replace(' ', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(c, '_');
        }

        const int maxLen = 80;
        if (safe.Length > maxLen)
        {
            safe = safe[..maxLen];
        }

        return safe;
    }

    private static string ShortHash(string value)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
    }
}
