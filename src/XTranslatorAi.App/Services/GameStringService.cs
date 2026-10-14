using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.Services;

/// <summary>
/// Keeps the player's game translation dictionary (see <see cref="GameStringDictionary"/>) in the global folder: built
/// once from the game's Data folder and the Korean patch's string tables, then loaded for each Skyrim translation run.
/// </summary>
public sealed class GameStringService
{
    private readonly string? _globalRootOverride;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameStringDictionary? _cached;
    private DateTime _cachedWriteUtc;

    public GameStringService(string? globalRootOverride = null) => _globalRootOverride = globalRootOverride;

    public string DictionaryPath => Path.Combine(ProjectPaths.GetGlobalRootDir(_globalRootOverride), "game-strings-skyrim.gz");

    public async Task<GameStringBuildReport> BuildAsync(string gameDataFolder, string koreanStringsFolder, CancellationToken cancellationToken)
    {
        var (dictionary, report) = await Task.Run(
            () => GameStringDictionaryBuilder.BuildAsync(gameDataFolder, koreanStringsFolder, cancellationToken), cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(DictionaryPath)!);
        var temp = DictionaryPath + ".partial";
        await using (var stream = File.Create(temp))
        {
            dictionary.Save(stream);
        }

        File.Move(temp, DictionaryPath, overwrite: true);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cached = dictionary;
            _cachedWriteUtc = File.GetLastWriteTimeUtc(DictionaryPath);
        }
        finally
        {
            _gate.Release();
        }

        return report;
    }

    /// <summary>The dictionary if one was built, reloaded when the file changed; null when there is none or it cannot be read.</summary>
    public async Task<GameStringDictionary?> TryLoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(DictionaryPath))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var written = File.GetLastWriteTimeUtc(DictionaryPath);
            if (_cached != null && written == _cachedWriteUtc)
            {
                return _cached;
            }

            _cached = await Task.Run(() =>
            {
                using var stream = File.OpenRead(DictionaryPath);
                return GameStringDictionary.Load(stream);
            }, cancellationToken);
            _cachedWriteUtc = written;
            return _cached;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Write($"WARN 게임 번역 사전을 읽지 못해 없이 번역합니다: {ex.Message}");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> DescribeAsync(CancellationToken cancellationToken)
    {
        if (await TryLoadAsync(cancellationToken) is not { } dictionary)
        {
            return "게임 번역 사전 없음";
        }

        return $"게임 번역 사전: 레코드 {dictionary.FieldCount:N0}개 · 문장 {dictionary.TextCount:N0}개 ({File.GetLastWriteTime(DictionaryPath):yyyy-MM-dd} 만듦)";
    }

    /// <summary>Skyrim SE's Data folder in a Steam library, or null when Steam does not list one.</summary>
    public static string? DetectGameDataFolder()
    {
        var steam = new List<string>();
        try
        {
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string path)
            {
                steam.Add(path.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
        }

        steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        var libraries = new List<string>();
        foreach (var root in steam.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }

        return libraries
            .Select(library => Path.Combine(library, "steamapps", "common", "Skyrim Special Edition", "Data"))
            .FirstOrDefault(data => File.Exists(Path.Combine(data, "Skyrim.esm")));
    }
}
