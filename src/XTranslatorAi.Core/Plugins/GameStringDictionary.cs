using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Plugins;

/// <summary>A game translation found for a row, and where it came from (shown in the row's note).</summary>
public sealed record GameStringMatch(string Text, string Origin);

/// <summary>One base game plugin read with its English tables, and the Korean patch's tables for it.</summary>
public sealed record GameStringPlugin(
    string FileName,
    IReadOnlyList<string> Masters,
    IReadOnlyList<PluginField> Fields,
    IReadOnlyDictionary<(PluginStringTableKind Kind, uint Id), string> Korean);

/// <summary>
/// The player's own game translation, as xTranslator's dictionaries use it: the base game's English strings paired
/// with the installed Korean patch by string ID. A mod row that overrides a base game record and keeps its English
/// text takes that record's Korean (dialogue too, since the speaker is the same); other rows take the Korean of the
/// same English text, except dialogue lines, which the patch words by speaker. Built from the user's files, so the
/// patch itself is never shipped with the app.
/// </summary>
public sealed class GameStringDictionary
{
    private const string FormatHeader = "XTranslatorAi-GameStrings-v1";
    private static readonly Regex Hangul = new("[가-힣]", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, (string English, string Korean, string Plugin)> _byField;
    private readonly Dictionary<string, string> _byText;

    private GameStringDictionary(Dictionary<string, (string, string, string)> byField, Dictionary<string, string> byText)
    {
        _byField = byField;
        _byText = byText;
    }

    public int FieldCount => _byField.Count;

    public int TextCount => _byText.Count;

    /// <summary>
    /// The record field behind a plugin field key ("INFO/01012345/0/NAM1/0"), named by the plugin that defines the record
    /// instead of the load-order index: "INFO/skyrim.esm:012345/0/NAM1/0". Null for a key that is not a record field.
    /// </summary>
    public static string? FieldIdentity(string fieldKey, IReadOnlyList<string> masters, string pluginFileName)
    {
        var parts = fieldKey.Split('/');
        if (parts.Length != 5 || !uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var formId))
        {
            return null;
        }

        var index = (int)(formId >> 24);
        var owner = index < masters.Count ? masters[index] : pluginFileName;
        return string.Create(CultureInfo.InvariantCulture,
            $"{parts[0]}/{owner.ToLowerInvariant()}:{formId & 0x00FFFFFFu:X6}/{parts[2]}/{parts[3]}/{parts[4]}");
    }

    /// <summary>Plugins in load order: a later plugin's text for a record (Update, DLC) replaces the earlier one.</summary>
    public static GameStringDictionary Build(IEnumerable<GameStringPlugin> plugins)
    {
        var byField = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
        var textVariants = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            foreach (var field in plugin.Fields)
            {
                if (field.TableKind is not { } kind || field.StringId is not { } id
                    || !plugin.Korean.TryGetValue((kind, id), out var korean) || !Hangul.IsMatch(korean)
                    || FieldIdentity(field.Key, plugin.Masters, plugin.FileName) is not { } identity)
                {
                    continue;
                }

                byField[identity] = (field.SourceText, korean, plugin.FileName);
                if (kind == PluginStringTableKind.IlStrings || IsDialogue(field.Rec))
                {
                    continue;
                }

                var key = TranslationMemoryKey.NormalizeSource(field.SourceText);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!textVariants.TryGetValue(key, out var variants))
                {
                    variants = new Dictionary<string, int>(StringComparer.Ordinal);
                    textVariants[key] = variants;
                }

                variants[korean] = variants.GetValueOrDefault(korean) + 1;
            }
        }

        // The spelling the patch uses most; a tie is ambiguous and left to the model.
        var byText = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, variants) in textVariants)
        {
            var ranked = variants.OrderByDescending(v => v.Value).ToList();
            if (ranked.Count == 1 || ranked[0].Value > ranked[1].Value)
            {
                byText[key] = ranked[0].Key;
            }
        }

        return new GameStringDictionary(byField, byText);
    }

    /// <summary>
    /// The game translation for a row: by its record field when it overrides a base game record with the same English,
    /// otherwise by its text unless it is dialogue.
    /// </summary>
    public GameStringMatch? Find(string? fieldIdentity, string? rec, string sourceText)
    {
        var key = TranslationMemoryKey.NormalizeSource(sourceText);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (fieldIdentity != null && _byField.TryGetValue(fieldIdentity, out var field)
            && string.Equals(TranslationMemoryKey.NormalizeSource(field.English), key, StringComparison.Ordinal))
        {
            return new GameStringMatch(field.Korean, $"{field.Plugin} 같은 레코드");
        }

        return !IsDialogue(rec) && _byText.TryGetValue(key, out var korean) ? new GameStringMatch(korean, "같은 문장") : null;
    }

    /// <summary>The game translations for the given rows of a project, by row id.</summary>
    public async System.Threading.Tasks.Task<IReadOnlyDictionary<long, GameStringMatch>> MatchAsync(
        XTranslatorAi.Core.Data.IProjectDb db,
        IReadOnlyList<long> ids,
        System.Threading.CancellationToken cancellationToken)
    {
        var rows = await db.GetStringTranslationContextsByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        var plugin = await db.GetPluginMastersAsync(cancellationToken).ConfigureAwait(false);
        var keys = plugin == null
            ? new Dictionary<long, string>()
            : await db.GetPluginFieldKeysByStringIdAsync(cancellationToken).ConfigureAwait(false);
        var matches = new Dictionary<long, GameStringMatch>();
        foreach (var (id, row) in rows)
        {
            var identity = plugin is { } p && keys.TryGetValue(id, out var key) ? FieldIdentity(key, p.Masters, p.PluginFileName) : null;
            if (Find(identity, row.Rec, row.SourceText) is { } match)
            {
                matches[id] = match;
            }
        }

        return matches;
    }

    private static bool IsDialogue(string? rec)
    {
        var recordType = (rec ?? "").Split(':')[0].Trim();
        return recordType.Equals("INFO", StringComparison.OrdinalIgnoreCase) || recordType.Equals("DIAL", StringComparison.OrdinalIgnoreCase);
    }

    public void Save(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
        writer.Write(FormatHeader);
        writer.Write('\n');
        foreach (var (identity, (english, korean, plugin)) in _byField)
        {
            writer.Write(string.Join('\t', "F", identity, Escape(english), Escape(korean), plugin));
            writer.Write('\n');
        }

        foreach (var (key, korean) in _byText)
        {
            writer.Write(string.Join('\t', "T", Escape(key), Escape(korean)));
            writer.Write('\n');
        }
    }

    public static GameStringDictionary Load(Stream stream)
    {
        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        if (reader.ReadLine() != FormatHeader)
        {
            throw new InvalidDataException("게임 번역 사전의 형식을 알 수 없습니다. 사전을 다시 만드세요.");
        }

        var byField = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
        var byText = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 5 && parts[0] == "F")
            {
                byField[parts[1]] = (Unescape(parts[2]), Unescape(parts[3]), parts[4]);
            }
            else if (parts.Length == 3 && parts[0] == "T")
            {
                byText[Unescape(parts[1])] = Unescape(parts[2]);
            }
        }

        return new GameStringDictionary(byField, byText);
    }

    private static string Escape(string text)
        => text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Unescape(string text)
    {
        if (text.IndexOf('\\') < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 == text.Length)
            {
                sb.Append(text[i]);
                continue;
            }

            sb.Append(text[++i] switch { 't' => '\t', 'r' => '\r', 'n' => '\n', var c => c });
        }

        return sb.ToString();
    }
}
