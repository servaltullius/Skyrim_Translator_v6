using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Plugins;

/// <summary>One string of the Korean patch with the game's English and the first record that shows it.</summary>
/// <param name="OverriddenBy">The later plugin whose override of every record using this string the game shows instead.</param>
public sealed record KoreanPatchString(string Plugin, PluginStringTableKind Table, uint Id, string Rec, string? Edid, string English, string Korean,
    string? OverriddenBy = null);

/// <summary>A reviewed change: the Korean the reviewer saw, and the Korean to write.</summary>
public sealed record KoreanPatchChange(string Plugin, PluginStringTableKind Table, uint Id, string Current, string Replacement);

/// <summary>What happened to each change: written, or skipped with the reason.</summary>
public sealed record KoreanPatchApplyResult(IReadOnlyList<KoreanPatchChange> Applied, IReadOnlyList<(KoreanPatchChange Change, string Reason)> Skipped);

/// <summary>
/// The community Korean patch the app's game translation dictionary is built from, read and edited string by string.
/// Reviews (with Gemini and by hand) are written back into its string tables, so the next dictionary build, and a
/// player who installs the tables, get the improved wording.
/// </summary>
public static class KoreanPatchStrings
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Every patch string that a record of the given plugins uses (the base game's when none are named), with the record
    /// that shows it. A Creation Club plugin's English comes from its own BSA.
    /// </summary>
    public static async Task<IReadOnlyList<KoreanPatchString>> ReadAsync(string gameDataFolder, string patchFolder, CancellationToken cancellationToken,
        IReadOnlyList<string>? plugins = null)
    {
        var folder = ResolveStringsFolder(patchFolder);
        var strings = new List<(KoreanPatchString String, List<string> Fields)>();
        // The plugins, in load order, that hold each record field: the game shows the last one's text.
        var holders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in plugins ?? GameStringDictionaryBuilder.BaseGamePlugins)
        {
            var path = Path.Combine(gameDataFolder, name);
            var tables = ReadTables(folder, Path.GetFileNameWithoutExtension(name));
            if (!File.Exists(path) || tables.Count == 0)
            {
                continue;
            }

            var document = await PluginReader.ReadAsync(path, new PluginReadOptions(), cancellationToken).ConfigureAwait(false);
            var byId = new Dictionary<(PluginStringTableKind, uint), int>();
            foreach (var field in document.Fields)
            {
                if (field.TableKind is not { } kind || field.StringId is not { } id)
                {
                    continue;
                }

                var identity = GameStringDictionary.FieldIdentity(field.Key, document.Info.Masters, name);
                if (identity != null)
                {
                    holders[identity] = name;
                }

                if (byId.TryGetValue((kind, id), out var index))
                {
                    strings[index].Fields.Add(identity ?? "");
                }
                else if (tables.TryGetValue(kind, out var table) && table.Strings.TryGetValue(id, out var korean))
                {
                    byId[(kind, id)] = strings.Count;
                    strings.Add((new KoreanPatchString(name, kind, id, field.Rec, field.EditorId, field.SourceText, korean), new List<string> { identity ?? "" }));
                }
            }
        }

        // A string still shown through one of its records is not hidden.
        return strings.Select(entry =>
        {
            var shownBy = entry.Fields.Select(identity => holders.GetValueOrDefault(identity, entry.String.Plugin)).Distinct().ToList();
            return shownBy.Contains(entry.String.Plugin) ? entry.String : entry.String with { OverriddenBy = shownBy[^1] };
        }).ToList();
    }

    /// <summary>
    /// Writes the changes into the patch's tables. A change is skipped when the patch no longer holds the Korean the
    /// reviewer saw, or when the new text loses the English's tags and placeholders. Nothing is written when
    /// <paramref name="write"/> is false. The tables written are first copied to <paramref name="backupFolder"/>.
    /// </summary>
    public static async Task<KoreanPatchApplyResult> ApplyAsync(
        string gameDataFolder,
        string patchFolder,
        IReadOnlyList<KoreanPatchChange> changes,
        bool write,
        string? backupFolder,
        CancellationToken cancellationToken)
    {
        // A change to another plugin's table (Creation Club) is checked against that plugin's English too.
        var plugins = GameStringDictionaryBuilder.BaseGamePlugins
            .Concat(changes.Select(c => c.Plugin).Where(p => !GameStringDictionaryBuilder.BaseGamePlugins.Contains(p, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var english = (await ReadAsync(gameDataFolder, patchFolder, cancellationToken, plugins).ConfigureAwait(false))
            .ToDictionary(s => (s.Plugin.ToLowerInvariant(), s.Table, s.Id), s => s.English);
        var folder = ResolveStringsFolder(patchFolder);
        var applied = new List<KoreanPatchChange>();
        var skipped = new List<(KoreanPatchChange, string)>();
        foreach (var group in changes.GroupBy(c => (Plugin: Path.GetFileNameWithoutExtension(c.Plugin), c.Table)))
        {
            var file = FindTable(folder, group.Key.Plugin, group.Key.Table);
            if (file == null)
            {
                skipped.AddRange(group.Select(c => (c, "패치에 이 테이블이 없음")));
                continue;
            }

            var table = PluginStringTable.Read(group.Key.Table, await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false), StrictUtf8);
            var replacements = new Dictionary<uint, string>();
            foreach (var change in group)
            {
                if (!table.Strings.TryGetValue(change.Id, out var current))
                {
                    skipped.Add((change, "패치에 이 ID가 없음"));
                }
                else if (!string.Equals(current, change.Current, StringComparison.Ordinal))
                {
                    skipped.Add((change, "현재 번역이 검수 때와 다름"));
                }
                else if (string.IsNullOrWhiteSpace(change.Replacement) || string.Equals(current, change.Replacement, StringComparison.Ordinal))
                {
                    skipped.Add((change, "바꿀 내용 없음"));
                }
                // A patch text whose tags already differ from the English (books laid out anew) is held to its own tags,
                // unless the change brings them closer to the English (a broken "]i>" repaired).
                else if (english.TryGetValue((change.Plugin.ToLowerInvariant(), change.Table, change.Id), out var source)
                         && TagProblem(source, change.Replacement) is { } problem
                         && (TagProblem(source, current) == null
                             || TagProblem(current, change.Replacement) != null && !BringsTagsCloser(source, current, change.Replacement)))
                {
                    skipped.Add((change, "태그 불일치: " + problem));
                }
                else
                {
                    replacements[change.Id] = change.Replacement;
                    applied.Add(change);
                }
            }

            if (write && replacements.Count > 0)
            {
                if (backupFolder != null)
                {
                    Directory.CreateDirectory(backupFolder);
                    var backup = Path.Combine(backupFolder, Path.GetFileName(file));
                    if (!File.Exists(backup))
                    {
                        File.Copy(file, backup);
                    }
                }

                var temp = file + ".partial";
                await File.WriteAllBytesAsync(temp, table.Write(replacements, StrictUtf8), cancellationToken).ConfigureAwait(false);
                File.Move(temp, file, overwrite: true);
            }
        }

        return new KoreanPatchApplyResult(applied, skipped);
    }

    private static readonly System.Text.RegularExpressions.Regex RawTag = new(@"<[^<>]+>|" + XTranslatorAi.Core.Text.ProtectedTextKinds.BracketPageBreakPattern,
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static readonly System.Text.RegularExpressions.Regex FormattingTag = new(@"^</?(?:p|font|b|i|u|div|br|img|span)\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Fewer tags differ from the English than before, and none is added that neither the English nor the patch had.
    // Formatting tags count together, since a translation may split a centered verse in two; a placeholder the game
    // fills in (<mag>, <Alias=...>) or a page break is never further from the English than before. The text the
    // validator protects counts too (a printf %% in a message, line breaks), under its own keys.
    private static bool BringsTagsCloser(string english, string current, string replacement)
    {
        static Dictionary<string, int> Tags(string text) => RawTag.Matches(text).Select(m => m.Value)
            .Concat(new XTranslatorAi.Core.Text.PlaceholderMasker().Mask(text).TokenToOriginal.Values.Select(v => "protected:" + v))
            .GroupBy(t => t, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        static int Gap(Dictionary<string, int> a, Dictionary<string, int> b, string tag) => Math.Abs(a.GetValueOrDefault(tag) - b.GetValueOrDefault(tag));

        var (source, before, after) = (Tags(english), Tags(current), Tags(replacement));
        var all = source.Keys.Union(before.Keys).Union(after.Keys).ToList();
        return after.Keys.All(tag => source.ContainsKey(tag) || before.ContainsKey(tag))
            && all.Where(tag => !FormattingTag.IsMatch(tag.StartsWith("protected:", StringComparison.Ordinal) ? tag["protected:".Length..] : tag))
                .All(tag => Gap(after, source, tag) <= Gap(before, source, tag))
            && all.Sum(tag => Gap(after, source, tag)) < all.Sum(tag => Gap(before, source, tag));
    }

    private static string? TagProblem(string english, string korean)
    {
        try
        {
            TokenValidator.ValidateFinalTextIntegrity(english, korean, context: "patch review");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }

    private static string ResolveStringsFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException("한글패치 폴더를 찾지 못했습니다: " + folder);
        }

        return Directory.GetDirectories(folder).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Strings", StringComparison.OrdinalIgnoreCase))
               ?? folder;
    }

    private static string? FindTable(string folder, string plugin, PluginStringTableKind kind)
        => Directory.GetFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f),
            $"{plugin}_english.{PluginReader.Extension(kind)}", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<PluginStringTableKind, PluginStringTable> ReadTables(string folder, string plugin)
    {
        var tables = new Dictionary<PluginStringTableKind, PluginStringTable>();
        foreach (var kind in Enum.GetValues<PluginStringTableKind>())
        {
            if (FindTable(folder, plugin, kind) is { } file)
            {
                tables[kind] = PluginStringTable.Read(kind, File.ReadAllBytes(file), StrictUtf8);
            }
        }

        return tables;
    }
}
