using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Plugins;

/// <summary>What a build read: the plugins found, and those skipped because the game or the patch lacks them.</summary>
public sealed record GameStringBuildReport(IReadOnlyList<string> Plugins, IReadOnlyList<string> Skipped, int FieldCount, int TextCount);

/// <summary>
/// Builds <see cref="GameStringDictionary"/> from the player's files: the base game masters in the game's Data folder
/// (their English tables come from Data\Strings or the game's interface archive, as for any localized plugin) and the
/// Korean patch's string tables, which keep the English file names (Skyrim_english.STRINGS).
/// </summary>
public static class GameStringDictionaryBuilder
{
    public static readonly IReadOnlyList<string> BaseGamePlugins = new[] { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };

    public static async Task<(GameStringDictionary Dictionary, GameStringBuildReport Report)> BuildAsync(
        string gameDataFolder,
        string koreanStringsFolder,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(Path.Combine(gameDataFolder, "Skyrim.esm")))
        {
            throw new FileNotFoundException("게임 Data 폴더에 Skyrim.esm이 없습니다. 스카이림 SE의 Data 폴더를 고르세요.");
        }

        var korean = ResolveStringsFolder(koreanStringsFolder);
        var plugins = new List<GameStringPlugin>();
        var found = new List<string>();
        var skipped = new List<string>();
        foreach (var name in BaseGamePlugins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(gameDataFolder, name);
            var tables = ReadKoreanTables(korean, Path.GetFileNameWithoutExtension(name));
            if (!File.Exists(path) || tables.Count == 0)
            {
                skipped.Add(name);
                continue;
            }

            var document = await PluginReader.ReadAsync(path, new PluginReadOptions(), cancellationToken).ConfigureAwait(false);
            plugins.Add(new GameStringPlugin(name, document.Info.Masters, document.Fields, tables));
            found.Add(name);
        }

        if (!found.Contains("Skyrim.esm"))
        {
            throw new FileNotFoundException("한글패치 폴더에서 Skyrim_english.STRINGS를 찾지 못했습니다. 한글패치의 Strings 폴더를 고르세요.");
        }

        var dictionary = GameStringDictionary.Build(plugins);
        return (dictionary, new GameStringBuildReport(found, skipped, dictionary.FieldCount, dictionary.TextCount));
    }

    // The Strings folder itself, or a mod folder that contains it (Mod Organizer keeps a patch as mods\<name>\Strings).
    private static string ResolveStringsFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("한글패치 폴더를 찾지 못했습니다: " + folder);
        }

        var nested = Directory.GetDirectories(folder).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Strings", StringComparison.OrdinalIgnoreCase));
        return nested ?? folder;
    }

    private static Dictionary<(PluginStringTableKind Kind, uint Id), string> ReadKoreanTables(string folder, string plugin)
    {
        var strings = new Dictionary<(PluginStringTableKind, uint), string>();
        foreach (var kind in Enum.GetValues<PluginStringTableKind>())
        {
            var file = Directory.GetFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f),
                $"{plugin}_english.{PluginReader.Extension(kind)}", StringComparison.OrdinalIgnoreCase));
            if (file == null)
            {
                continue;
            }

            var table = PluginStringTable.Read(kind, File.ReadAllBytes(file), new UTF8Encoding(false, true));
            foreach (var (id, text) in table.Strings)
            {
                strings[(kind, id)] = text;
            }
        }

        return strings;
    }
}
