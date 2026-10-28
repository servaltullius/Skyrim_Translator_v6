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
/// The game translation dictionary (see <see cref="GameStringDictionary"/>) for Skyrim translation runs: the app ships
/// one built from the community Korean patch, and a player who uses another revision of the patch can build their own
/// from the game's Data folder and the patch's string tables; it is kept in the global folder and used instead.
/// </summary>
public sealed class GameStringService
{
    private readonly string? _globalRootOverride;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<Stream?> _openBundled;
    private GameStringDictionary? _cached;
    private DateTime _cachedWriteUtc;
    private GameStringDictionary? _bundled;
    // The built dictionary of this write time could not be read: not read again (each call re-read it and logged a WARN).
    private DateTime? _unreadableWriteUtc;
    private string? _unreadableReason;

    /// <param name="openBundled">The dictionary the app ships (built from the community Korean patch); null for none.</param>
    public GameStringService(string? globalRootOverride = null, Func<Stream?>? openBundled = null)
    {
        _globalRootOverride = globalRootOverride;
        _openBundled = openBundled ?? EmbeddedAssets.OpenBundledGameStrings;
    }

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

    /// <summary>
    /// The dictionary the player built (reloaded when the file changed), else the one the app ships; null when there is
    /// neither or it cannot be read.
    /// </summary>
    public async Task<GameStringDictionary?> TryLoadAsync(CancellationToken cancellationToken)
        => (await TryLoadWithSourceAsync(cancellationToken)).Dictionary;

    private async Task<(GameStringDictionary? Dictionary, bool Built)> TryLoadWithSourceAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(DictionaryPath))
            {
                var written = File.GetLastWriteTimeUtc(DictionaryPath);
                if (written != _unreadableWriteUtc)
                {
                    try
                    {
                        if (_cached == null || written != _cachedWriteUtc)
                        {
                            _cached = await Task.Run(() =>
                            {
                                using var stream = File.OpenRead(DictionaryPath);
                                return GameStringDictionary.Load(stream);
                            }, cancellationToken);
                            _cachedWriteUtc = written;
                        }

                        return (_cached, true);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                    {
                        // A dictionary the player built that cannot be read (damaged, or made by a build before its format)
                        // left the run with no game text at all: the one the app ships is used, and the window says so.
                        AppLog.Write($"WARN 직접 만든 게임 번역 사전을 읽지 못해 기본 사전을 씁니다: {ex.Message}");
                        _cached = null;
                        // Only a file that is not a dictionary is kept from: one held a moment (an antivirus, a backup) is
                        // read again next time.
                        if (ex is InvalidDataException or EndOfStreamException)
                        {
                            (_unreadableWriteUtc, _unreadableReason) = (written, ex.Message);
                        }
                    }
                }
            }

            _bundled ??= await Task.Run(() =>
            {
                using var stream = _openBundled();
                return stream == null ? null : GameStringDictionary.Load(stream);
            }, cancellationToken);
            return (_bundled, false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLog.Write($"WARN 게임 번역 사전을 읽지 못해 없이 번역합니다: {ex.Message}");
            return (null, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> DescribeAsync(CancellationToken cancellationToken)
    {
        var (dictionary, built) = await TryLoadWithSourceAsync(cancellationToken);
        if (dictionary == null)
        {
            return "게임 번역 사전 없음";
        }

        var counts = $"레코드 {dictionary.FieldCount:N0}개 · 문장 {dictionary.TextCount:N0}개";
        if (!built && _unreadableReason is { } reason && File.Exists(DictionaryPath))
        {
            return $"직접 만든 게임 번역 사전을 읽지 못해({reason}) 기본 사전(한글패치)을 씁니다: {counts}. 사전을 다시 만드세요";
        }

        return built
            ? $"직접 만든 게임 번역 사전: {counts} ({File.GetLastWriteTime(DictionaryPath):yyyy-MM-dd} 만듦)"
              + (dictionary.HasBaseBlocks ? "" : ". 1.16.17 전에 만든 사전이라 바닐라 책 사본에 한글패치 문단을 끼워 넣지 못합니다. 사전을 다시 만드세요")
            : $"기본 게임 번역 사전(한글패치): {counts}";
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
