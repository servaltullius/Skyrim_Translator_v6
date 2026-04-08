using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public static class ProjectPaths
{
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

    public static string GetProjectDbPath(string addonName, string sourceLang, string destLang)
    {
        var baseDir = GetProjectsBaseDir();
        Directory.CreateDirectory(baseDir);

        var addonStem = Path.GetFileNameWithoutExtension((addonName ?? "").Trim());
        var safeAddon = SanitizeFileNameStem(addonStem, fallback: "project");
        var safeSourceLang = SanitizeFileNameStem(sourceLang, fallback: "src");
        var safeDestLang = SanitizeFileNameStem(destLang, fallback: "dst");
        var hash = ShortHash($"{addonName}|{sourceLang}|{destLang}");

        return Path.Combine(baseDir, $"{safeAddon}.{safeSourceLang}-{safeDestLang}.{hash}.sqlite");
    }

    public static string GetLegacyProjectDbPath(string inputXmlPath)
    {
        var baseDir = GetProjectsBaseDir();
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
