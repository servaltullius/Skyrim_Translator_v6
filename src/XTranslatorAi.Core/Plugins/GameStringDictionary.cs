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

/// <summary>
/// A game translation found for a row, and where it came from (shown in the row's note). For a base game text the mod
/// copied with changes, the row's paragraphs the base game does not have (see GameStringDictionary.FindSimilar).
/// </summary>
public sealed record GameStringMatch(string Text, string Origin, IReadOnlyList<string>? ChangedParagraphs = null,
    IReadOnlySet<string>? BaseParagraphKeys = null);

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
/// same English text, except dialogue lines, which the patch words by speaker. The English is kept only as a
/// fingerprint (the text is Bethesda's), so the app can ship a dictionary built from the community Korean patch; a
/// player can build one from the patch they installed.
/// </summary>
public sealed class GameStringDictionary
{
    // v2 keeps a fingerprint of the English instead of the text, so the dictionary can ship with the app.
    private const string FormatHeader = "XTranslatorAi-GameStrings-v2";
    private static readonly Regex Hangul = new("[가-힣]", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, (string EnglishHash, string Korean, string Plugin)> _byField;
    private readonly Dictionary<string, string> _byText;
    private readonly Dictionary<string, string[]> _paragraphs;
    private readonly Dictionary<string, List<string>> _byParagraph;

    private GameStringDictionary(Dictionary<string, (string, string, string)> byField, Dictionary<string, string> byText,
        MasterSpeakerIndex? speakers = null, Dictionary<string, string[]>? paragraphs = null)
    {
        _byField = byField;
        _byText = byText;
        Speakers = speakers;
        _paragraphs = paragraphs ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
        _byParagraph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (identity, keys) in _paragraphs)
        {
            foreach (var key in keys.Distinct(StringComparer.Ordinal))
            {
                if (!_byParagraph.TryGetValue(key, out var owners)) _byParagraph[key] = owners = new List<string>();
                owners.Add(identity);
            }
        }
    }

    /// <summary>Who speaks a mod's lines where only the masters can say (null in a dictionary built before it existed).</summary>
    public MasterSpeakerIndex? Speakers { get; }

    /// <summary>This dictionary with the masters' speaker index; the translations are shared, not copied.</summary>
    public GameStringDictionary WithSpeakers(MasterSpeakerIndex? speakers) => new(_byField, _byText, speakers, _paragraphs);

    /// <summary>The long texts whose paragraphs are kept, for finding a mod's edited copy (see <see cref="FindSimilar"/>).</summary>
    public int ParagraphTextCount => _paragraphs.Count;

    /// <summary>
    /// This dictionary with the paragraph fingerprints of the base game's long texts (books, notes), each kept only
    /// where the record's English is the one its Korean translates. The translations are shared, not copied.
    /// </summary>
    public GameStringDictionary WithParagraphs(IEnumerable<(string Identity, string? Rec, string English)> fields)
    {
        var paragraphs = new Dictionary<string, string[]>(_paragraphs, StringComparer.Ordinal);
        foreach (var (identity, rec, english) in fields)
        {
            if (IsDialogue(rec) || !_byField.TryGetValue(identity, out var field)) continue;
            var normalized = TranslationMemoryKey.NormalizeSource(english);
            if (string.IsNullOrWhiteSpace(normalized) || !string.Equals(field.EnglishHash, Fingerprint(normalized), StringComparison.Ordinal)) continue;
            var keys = ParagraphKeys(english);
            if (keys.Count >= MinSharedParagraphs) paragraphs[identity] = keys.ToArray();
        }

        return new GameStringDictionary(_byField, _byText, Speakers, paragraphs);
    }

    // A long text counts as the base game's when this many of its paragraphs, and this share of both texts', are the same.
    private const int MinSharedParagraphs = 3;
    private const double MinSharedShare = 0.6;
    private const int MinParagraphChars = 40;
    private static readonly Regex ParagraphMarkup = new(@"<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex PageBreak = new(@"\[page ?break\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Fingerprints of a text's paragraphs, without markup, case or spacing: LotD's copy of The Falmer: A Study drops
    /// the base game's trailing spaces and fixes a typo ("the overthrew"), so its whole text never matches, but all but
    /// one paragraph do.
    /// </summary>
    internal static IReadOnlyList<string> ParagraphKeys(string english) => Paragraphs(english).Select(p => p.Key).ToList();

    // Each paragraph long enough to tell texts apart: its fingerprint, and its text with the spacing collapsed.
    private static List<(string Key, string Text)> Paragraphs(string english)
    {
        var text = PageBreak.Replace(english.Replace("\r\n", "\n").Replace('\r', '\n'), "\n\n");
        text = ParagraphMarkup.Replace(text, "");
        var paragraphs = new List<(string, string)>();
        foreach (var paragraph in Regex.Split(text, @"\n\s*\n"))
        {
            var plain = Spaces.Replace(paragraph, " ").Trim();
            if (plain.Length >= MinParagraphChars) paragraphs.Add((Fingerprint(plain.ToLowerInvariant())[..16], plain));
        }

        return paragraphs;
    }

    /// <summary>
    /// The base game's Korean for a long text the mod copied with changes (whitespace, a fixed typo, an added line):
    /// most of its paragraphs are a base game text's. The match lists the row's paragraphs the base game text lacks;
    /// none means only spacing or markup differs. Dialogue is left out, as for <see cref="Find"/>.
    /// </summary>
    public GameStringMatch? FindSimilar(string? rec, string sourceText)
    {
        if (_byParagraph.Count == 0 || IsDialogue(rec)) return null;
        var paragraphs = Paragraphs(sourceText);
        var keys = paragraphs.Select(p => p.Key).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count < MinSharedParagraphs) return null;
        var shared = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!_byParagraph.TryGetValue(key, out var owners)) continue;
            foreach (var owner in owners) shared[owner] = shared.GetValueOrDefault(owner) + 1;
        }

        var best = shared
            .Where(s => s.Value >= MinSharedParagraphs && s.Value >= keys.Count * MinSharedShare
                        && s.Value >= _paragraphs[s.Key].Distinct(StringComparer.Ordinal).Count() * MinSharedShare)
            .OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => s.Key).FirstOrDefault();
        if (best == null || !_byField.TryGetValue(best, out var field)) return null;
        var baseKeys = _paragraphs[best].ToHashSet(StringComparer.Ordinal);
        var changed = paragraphs.Where(p => !baseKeys.Contains(p.Key)).Select(p => p.Text).Distinct(StringComparer.Ordinal).ToList();
        // A copy that dropped some of the base game's paragraphs is not the same text either.
        var dropped = shared[best] < baseKeys.Count;
        return new GameStringMatch(field.Korean, $"{field.Plugin} 비슷한 글 {shared[best]}/{keys.Count}문단",
            changed.Count == 0 && dropped ? new[] { "(the base game's text has paragraphs this source does not)" } : changed,
            baseKeys);
    }

    /// <summary>
    /// The base game's Korean for a long text the mod copied changing only spacing or markup: every paragraph is the
    /// same. Applied as the game's own text is, laid out with the source's blank lines, when its tags fit the row.
    /// LotD's Japhet's Journal has one blank line more than the base game's, and was translated anew for it.
    /// </summary>
    public GameStringMatch? FindSameParagraphs(string? rec, string sourceText)
        => FindSimilar(rec, sourceText) is { ChangedParagraphs.Count: 0 } match
            ? new GameStringMatch(Relayout(sourceText, match.Text) ?? match.Text, match.Origin.Split(' ')[0] + " 같은 글(공백·꾸밈만 다름)")
            : null;

    // Blank-line runs, kept by Regex.Split as the odd entries.
    private static readonly Regex BlankLines = new(@"(\r?\n[ \t]*\r?\n(?:[ \t]*\r?\n)*)", RegexOptions.CultureInvariant);

    // Tags and page breaks, kept by Regex.Split as the odd entries.
    private static readonly Regex TagPieces = new(@"(<[^>]*>|\[page ?break\])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The Korean laid out as the source is: the same tags in the same order, the Korean's words between them, and the
    /// source's spacing and line breaks around them (between blank-line blocks too, when the blocks pair up). The
    /// patch's Japhet's Journal breaks the line after its font tag where the English does not. Null when the tags differ.
    /// </summary>
    private static string? Relayout(string source, string korean)
    {
        var s = TagPieces.Split(source);
        var k = TagPieces.Split(korean);
        if (s.Length != k.Length) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i % 2 == 1)
            {
                if (!string.Equals(s[i], k[i], StringComparison.OrdinalIgnoreCase)) return null;
                sb.Append(s[i]);
                continue;
            }

            var core = k[i].Trim();
            if (core.Length == 0 != (s[i].Trim().Length == 0)) return null;
            var lead = s[i][..(s[i].Length - s[i].TrimStart().Length)];
            var trail = s[i].Length == lead.Length ? "" : s[i][s[i].TrimEnd().Length..];
            var sourceBlocks = BlankLines.Split(s[i].Trim());
            var koreanBlocks = BlankLines.Split(core);
            if (sourceBlocks.Length == koreanBlocks.Length)
            {
                core = string.Concat(koreanBlocks.Select((block, j) => j % 2 == 0 ? block.Trim() : sourceBlocks[j]));
            }

            sb.Append(lead).Append(core).Append(trail);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The model's translation of a base game text the mod copied with changes, with each block the mod left as the base
    /// game wrote it replaced by the Korean patch's block: told to copy the patch, gemini-3.8-flash still retranslated
    /// LotD's Falmer: A Study (암벽 전쟁 for the patch's 크래그 전쟁). Blocks pair up by position between blank lines; a
    /// block whose markup differs from the source's keeps the model's text. Null when nothing pairs up.
    /// </summary>
    public static string? SpliceUnchanged(string source, string translated, GameStringMatch match)
    {
        if (match.BaseParagraphKeys is not { } baseKeys) return null;
        var s = BlankLines.Split(source);
        var t = BlankLines.Split(translated);
        var k = BlankLines.Split(match.Text.Trim());
        if (s.Length != t.Length || s.Length != k.Length) return null;
        var spliced = false;
        for (var i = 0; i < s.Length; i += 2)
        {
            var keys = Paragraphs(s[i]).Select(p => p.Key).ToList();
            if (keys.Count == 0 || !keys.All(baseKeys.Contains)) continue;
            // The patch's block laid out as the source's, with the spacing the model kept around it.
            if (Relayout(s[i].Trim(), k[i].Trim()) is not { } block) continue;
            var lead = t[i][..(t[i].Length - t[i].TrimStart().Length)];
            var trail = t[i].Length == lead.Length ? "" : t[i][t[i].TrimEnd().Length..];
            t[i] = lead + block + trail;
            spliced = true;
        }

        return spliced ? string.Concat(t) : null;
    }

    /// <summary>For each given row that is a base game text or a copy of one with changes, that text's Korean, as a reference.</summary>
    public async System.Threading.Tasks.Task<IReadOnlyDictionary<long, GameStringMatch>> SimilarAsync(
        XTranslatorAi.Core.Data.IProjectDb db,
        IReadOnlyList<long> ids,
        System.Threading.CancellationToken cancellationToken)
    {
        var found = new Dictionary<long, GameStringMatch>();
        if (_byParagraph.Count == 0 || ids.Count == 0) return found;
        // Rows the game's text matches are included: when the patch's own line breaks or tags keep it from being applied
        // (211 of LotD's 1,410 books), the model translates the row with the reference, and the patch's blocks are
        // spliced in where they pair up.
        var rows = await db.GetStringTranslationContextsByIdsAsync(ids, cancellationToken).ConfigureAwait(false);
        foreach (var (id, row) in rows)
        {
            if (FindSimilar(row.Rec, row.SourceText) is { } match) found[id] = match;
        }

        return found;
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

    private const string CreationClubCellPlaceholder = "creation club cell"; // As TranslationMemoryKey.NormalizeSource has it.

    /// <summary>Plugins in load order: a later plugin's text for a record (Update, DLC) replaces the earlier one.</summary>
    public static GameStringDictionary Build(IEnumerable<GameStringPlugin> plugins)
    {
        var byField = new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);
        // Each record's text as the game shows it: a later plugin's override of the same English replaces the earlier
        // text, which no longer counts. Update.esm's 제련 for the Smithing skill was outvoted by the 뼈다귀 제련 it
        // replaces. An override that changes the English leaves the earlier pair a translation of its own English.
        var shownText = new Dictionary<(string Identity, string Key), string>();
        var english = new List<(string Identity, string? Rec, string English)>();
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

                var normalized = TranslationMemoryKey.NormalizeSource(field.SourceText);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                var key = Fingerprint(normalized);
                byField[identity] = (key, korean, plugin.FileName);
                english.Add((identity, field.Rec, field.SourceText));
                // Game 1.6 renamed some 1.5.97 strings "Creation Club Cell" for its hidden Creation Club cells, and the
                // patch translates such a string for what the 1.5.97 game shows (a Dragonborn effect still points at
                // one): the record keeps it, but the placeholder text translates no other record's name.
                if (kind == PluginStringTableKind.IlStrings || IsDialogue(field.Rec)
                    || string.Equals(normalized, CreationClubCellPlaceholder, StringComparison.Ordinal))
                {
                    continue;
                }

                shownText[(identity, key)] = korean;
            }
        }

        var textVariants = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var ((_, key), korean) in shownText)
        {
            if (!textVariants.TryGetValue(key, out var variants))
            {
                variants = new Dictionary<string, int>(StringComparer.Ordinal);
                textVariants[key] = variants;
            }

            variants[korean] = variants.GetValueOrDefault(korean) + 1;
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

        return new GameStringDictionary(byField, byText).WithParagraphs(english);
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

        key = Fingerprint(key);
        if (fieldIdentity != null && _byField.TryGetValue(fieldIdentity, out var field)
            && string.Equals(field.EnglishHash, key, StringComparison.Ordinal))
        {
            return new GameStringMatch(field.Korean, $"{field.Plugin} 같은 레코드");
        }

        return !IsDialogue(rec) && _byText.TryGetValue(key, out var korean) ? new GameStringMatch(korean, "같은 문장") : null;
    }

    /// <summary>
    /// The base game's Korean for a record field whatever its English now is, and whether the English is still the
    /// same: a patch that fixes a vanilla line is reviewed against the line it replaces.
    /// </summary>
    public (string Korean, bool SameEnglish)? FindRecord(string? fieldIdentity, string sourceText)
    {
        if (fieldIdentity == null || !_byField.TryGetValue(fieldIdentity, out var field))
        {
            return null;
        }

        var key = TranslationMemoryKey.NormalizeSource(sourceText);
        return (field.Korean, !string.IsNullOrWhiteSpace(key) && string.Equals(field.EnglishHash, Fingerprint(key), StringComparison.Ordinal));
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
            if (Find(identity, row.Rec, row.SourceText) is { } exact)
            {
                // Laid out as the row is: the patch's Japhet's Journal breaks the line after its font tag where the
                // English, the game's and the mod's alike, does not, so the game's own text never fit the row.
                matches[id] = Relayout(row.SourceText, exact.Text) is { } laidOut ? exact with { Text = laidOut } : exact;
            }
            else if (_byParagraph.Count > 0 && FindSameParagraphs(row.Rec, row.SourceText) is { } same)
            {
                matches[id] = same;
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
        foreach (var (identity, (englishHash, korean, plugin)) in _byField)
        {
            writer.Write(string.Join('\t', "F", identity, englishHash, Escape(korean), plugin));
            writer.Write('\n');
        }

        foreach (var (key, korean) in _byText)
        {
            writer.Write(string.Join('\t', "T", key, Escape(korean)));
            writer.Write('\n');
        }

        // Kinds an older app does not know are skipped by its reader, so the format keeps its version.
        foreach (var (identity, keys) in _paragraphs)
        {
            writer.Write(string.Join('\t', "P", identity, string.Join(',', keys)));
            writer.Write('\n');
        }

        foreach (var line in Speakers?.Lines() ?? Enumerable.Empty<string>())
        {
            writer.Write(line);
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
        var paragraphs = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var speakers = new MasterSpeakerIndex.Maps();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length == 5 && parts[0] == "F")
            {
                byField[parts[1]] = (parts[2], Unescape(parts[3]), parts[4]);
            }
            else if (parts.Length == 3 && parts[0] == "T")
            {
                byText[parts[1]] = Unescape(parts[2]);
            }
            else if (parts.Length == 3 && parts[0] == "P")
            {
                paragraphs[parts[1]] = parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                MasterSpeakerIndex.TryReadLine(parts, speakers);
            }
        }

        return new GameStringDictionary(byField, byText, MasterSpeakerIndex.FromMaps(speakers), paragraphs);
    }

    // 128 bits of SHA-256 over the normalized English: equal text gives an equal fingerprint, and no text can be read back.
    private static string Fingerprint(string normalizedEnglish)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEnglish)), 0, 16);

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
