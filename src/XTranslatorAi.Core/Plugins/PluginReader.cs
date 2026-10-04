using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Plugins;

public static class PluginReader
{
    public static async Task<PluginDocument> ReadAsync(string path, PluginReadOptions options, CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        options = options with
        {
            StringsDirectory = options.StringsDirectory == null ? null : Path.GetFullPath(options.StringsDirectory),
            ArchivePaths = options.ArchivePaths?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
        if (options.Game != PluginGame.SkyrimSpecialEdition) throw new NotSupportedException("확인되지 않은 게임 형식입니다.");
        ValidateLanguage(options.SourceLanguage);
        _ = PluginBinary.GetEncoding(options.SourceEncoding);
        _ = PluginBinary.GetEncoding(options.MetadataEncoding);
        if (!new[] { ".esp", ".esm", ".esl" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("ESP/ESM/ESL 파일을 선택하세요.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tables = new Dictionary<PluginStringTableKind, byte[]>();
        var sources = new Dictionary<PluginStringTableKind, string>();
        var structure = ParseStructure(bytes, cancellationToken);
        if (structure.Records[0].Flags.HasFlag(PluginBinary.LocalizedFlag))
        {
            var archives = ResolveArchives(path, options).ToArray();
            var gameInterface = FindGameInterfaceArchive(path, options, archives);
            var archiveLocks = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
            try
            {
            foreach (var kind in Enum.GetValues<PluginStringTableKind>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(path) + "_" + ValidateLanguage(options.SourceLanguage) + "." + Extension(kind);
                var folder = options.StringsDirectory ?? Path.Combine(Path.GetDirectoryName(path)!, "Strings");
                var tablePath = FindFile(folder, name);
                if (tablePath != null)
                {
                    var data = await File.ReadAllBytesAsync(tablePath, cancellationToken).ConfigureAwait(false);
                    tables[kind] = data;
                    dependencies[tablePath] = PluginBinary.Hash(data);
                    sources[kind] = tablePath;
                    continue;
                }
                foreach (var archive in archives)
                {
                    if (!archiveLocks.TryGetValue(archive, out var stream))
                    {
                        stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                        archiveLocks.Add(archive, stream);
                    }
                    var data = await PluginArchiveReader.ReadFileAsync(archive, "strings/" + name, cancellationToken).ConfigureAwait(false);
                    if (data == null) continue;
                    if (tables.TryGetValue(kind, out var existing) && !existing.AsSpan().SequenceEqual(data))
                        throw new InvalidDataException($"{name}의 내용이 BSA마다 다릅니다. 사용할 Strings 폴더 또는 하나의 BSA 경로를 명시하세요: {sources[kind]}, {archive}");
                    tables[kind] = data;
                    sources.TryAdd(kind, archive);
                    // Hold the archive stable while reading all tables, and hash it only once.
                    if (!dependencies.ContainsKey(archive))
                        dependencies[archive] = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
                }
                if (!tables.ContainsKey(kind) && gameInterface != null)
                {
                    // Skyrim SE keeps the tables of Update, Dawnguard, HearthFires and Dragonborn in the game's interface
                    // archive. It is only a fallback: read when no own archive has the table, and an archive the reader
                    // cannot handle (replaced by a mod) is skipped instead of stopping the plugin.
                    byte[]? data;
                    FileStream? stream = null;
                    try
                    {
                        stream = new FileStream(gameInterface, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                        data = await PluginArchiveReader.ReadFileAsync(gameInterface, "strings/" + name, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                    {
                        data = null;
                    }
                    if (data != null && stream != null)
                    {
                        tables[kind] = data;
                        sources[kind] = gameInterface;
                        if (!dependencies.ContainsKey(gameInterface))
                            dependencies[gameInterface] = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
                    }
                    if (stream != null && !archiveLocks.TryAdd(gameInterface, stream)) await stream.DisposeAsync();
                }
            }
            }
            finally
            {
                foreach (var stream in archiveLocks.Values) await stream.DisposeAsync();
            }
        }
        return ReadWithSourceEncodingFallback(path, bytes, options, tables, dependencies, structure, cancellationToken, sources);
    }

    /// <summary>
    /// English plugins are usually saved in Windows-1252, while the source encoding defaults to UTF-8, so a
    /// single "cliché" stopped the whole plugin from opening. When UTF-8 cannot decode the source text, the
    /// plugin is read again as Windows-1252 — unless some text was valid UTF-8, which a mixed file would
    /// turn into mojibake ("â€™"); that keeps the original error. Every byte sequence is valid Windows-1252,
    /// so an older Korean translation saved in CP949 would also open, "검을" as "°ËÀ»", and a UTF-8
    /// export would then write that mojibake over the Korean text. Such a file keeps the error too, with a
    /// hint that it looks like CP949 Korean.
    /// </summary>
    private static PluginDocument ReadWithSourceEncodingFallback(string path, byte[] bytes, PluginReadOptions options,
        IReadOnlyDictionary<PluginStringTableKind, byte[]> tables, IReadOnlyDictionary<string, string> dependencies,
        (IReadOnlyList<PluginNode> Nodes, IReadOnlyDictionary<int, PluginRecord> Records) structure,
        CancellationToken cancellationToken, IReadOnlyDictionary<PluginStringTableKind, string> sources)
    {
        try
        {
            return ReadSnapshot(path, bytes, options, tables, dependencies, structure, cancellationToken, sources);
        }
        catch (InvalidDataException utf8Error) when (utf8Error.InnerException is DecoderFallbackException
                                                      && GetSourceEncodingFallback(options.SourceEncoding) != null)
        {
            PluginDocument? fallback = null;
            try
            {
                fallback = ReadSnapshot(path, bytes, options with { SourceEncoding = Windows1252 }, tables, dependencies, structure,
                    cancellationToken, sources);
            }
            catch (InvalidDataException)
            {
                // Metadata or structure problems are not about the source encoding; report the original error.
            }

            var windows1252 = PluginBinary.GetEncoding(Windows1252);
            if (fallback != null && LooksLikeCp949Korean(SourceTexts(fallback), windows1252))
                throw new InvalidDataException(utf8Error.Message + Cp949KoreanHint, utf8Error.InnerException);
            if (fallback == null || SourceTexts(fallback).Any(text => WasValidUtf8(text, windows1252)))
            {
                throw;
            }

            var diagnostics = fallback.Info.Diagnostics.Append(new PluginDiagnostic("source_encoding_fallback",
                "원문이 UTF-8이 아니어서 windows-1252로 읽었습니다.")).ToArray();
            return new PluginDocument(fallback.Info with { Diagnostics = Array.AsReadOnly(diagnostics) }, fallback.Fields,
                fallback.Bytes, fallback.Nodes, fallback.Records, fallback.Tables, fallback.DependencyHashes);
        }
    }

    /// <summary>
    /// The encoding a plugin is read with when <paramref name="sourceEncoding"/> cannot decode it, or null when
    /// that setting has no fallback. Only the UTF-8 default falls back, to Windows-1252.
    /// </summary>
    public static string? GetSourceEncodingFallback(string sourceEncoding)
        => PluginBinary.GetEncoding(sourceEncoding).CodePage == Encoding.UTF8.CodePage ? Windows1252 : null;

    private const string Windows1252 = "windows-1252";

    /// <summary>Appended to the decoding error; PluginUserFacingErrorClassifier turns it into the ks_c_5601-1987 hint.</summary>
    internal const string Cp949KoreanHint = " CP949 한글 원문으로 보입니다.";

    /// <summary>Each source string once: inline fields, then every string-table entry (localized fields point into them).</summary>
    private static IEnumerable<string> SourceTexts(PluginDocument document)
        => document.Fields.Where(field => field.TableKind == null).Select(field => field.SourceText)
            .Concat(document.Tables.Values.SelectMany(table => table.Strings.Values));

    /// <summary>
    /// Whether text read as Windows-1252 is really CP949 Korean. Smart punctuation alone often forms valid
    /// CP949 pairs — "’s" (0x92 0x73) is the rare syllable "뭩" and "—a" is "뾞" — so only the 2,350 common
    /// KS X 1001 syllables (lead 0xB0-0xC8, trail 0xA1-0xFE) count. They must form at least one word of two
    /// syllables and make up most of the non-ASCII bytes, which Serana's "cliché" or an English "Don’t—ever"
    /// does not.
    /// </summary>
    private static bool LooksLikeCp949Korean(IEnumerable<string> texts, Encoding windows1252)
    {
        var cp949 = PluginBinary.GetEncoding("ks_c_5601-1987");
        var nonAsciiBytes = 0;
        var syllableBytes = 0;
        var hasWord = false;
        foreach (var text in texts)
        {
            if (text.All(char.IsAscii))
            {
                continue;
            }

            var bytes = windows1252.GetBytes(text);
            nonAsciiBytes += bytes.Count(value => value >= 0x80);
            try
            {
                _ = cp949.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                continue;
            }

            var run = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                // A valid CP949 string pairs every byte 0x81-0xFE with a trail byte; 0x80 and 0xFF stand alone.
                if (bytes[i] is < 0x81 or 0xFF)
                {
                    run = 0;
                    continue;
                }

                var lead = bytes[i];
                var trail = bytes[++i];
                if (lead is < 0xB0 or > 0xC8 || trail < 0xA1)
                {
                    run = 0;
                    continue;
                }

                syllableBytes += 2;
                hasWord |= ++run >= 2;
            }
        }

        return hasWord && syllableBytes * 2 > nonAsciiBytes;
    }

    private static bool WasValidUtf8(string text, Encoding windows1252)
    {
        if (text.All(char.IsAscii))
        {
            return false;
        }

        try
        {
            _ = PluginBinary.GetEncoding("utf-8").GetString(windows1252.GetBytes(text));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    internal static PluginDocument ReadSnapshot(string path, byte[] bytes, PluginReadOptions options,
        IReadOnlyDictionary<PluginStringTableKind, byte[]> tableBytes,
        IReadOnlyDictionary<string, string>? dependencies = null,
        (IReadOnlyList<PluginNode> Nodes, IReadOnlyDictionary<int, PluginRecord> Records)? parsed = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<PluginStringTableKind, string>? tableSources = null)
    {
        if (options.Game != PluginGame.SkyrimSpecialEdition) throw new NotSupportedException("확인되지 않은 게임 형식입니다.");
        ValidateLanguage(options.SourceLanguage);
        var encoding = PluginBinary.GetEncoding(options.SourceEncoding);
        var metadataEncoding = PluginBinary.GetEncoding(options.MetadataEncoding);
        var (nodes, records) = parsed ?? ParseStructure(bytes, cancellationToken);
        var header = records[0];
        var hedr = header.Subrecords.SingleOrDefault(sub => sub.Type == "HEDR");
        if (hedr == null || hedr.Data.Length != 12)
            throw new InvalidDataException("TES4 HEDR 헤더가 잘못되었습니다.");
        var version = BitConverter.Int32BitsToSingle(unchecked((int)PluginBinary.U32(hedr.Data.Span, 0)));
        var localized = (header.Flags & PluginBinary.LocalizedFlag) != 0;
        var masters = header.Subrecords.Where(sub => sub.Type == "MAST")
            .Select(sub => PluginBinary.ReadZString(sub.Data.Span, metadataEncoding, "MAST")).ToArray();
        var skyrimLe = IsSkyrimLeHeader(version, masters, header.Raw.Span);
        if (!skyrimLe && (!float.IsFinite(version) || (Math.Abs(version - 1.7f) > 0.0001f && Math.Abs(version - 1.71f) > 0.0001f)))
            throw new NotSupportedException($"Skyrim 형식 HEDR 1.7/1.71이 아닙니다({version}). 다른 게임의 파일을 Skyrim 형식으로 저장할 수 없습니다.");
        var tables = tableBytes.ToDictionary(pair => pair.Key, pair => PluginStringTable.Read(pair.Key, pair.Value, encoding));
        var fields = new List<PluginField>();
        var diagnostics = new List<PluginDiagnostic>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        if (skyrimLe)
            diagnostics.Add(new("skyrim_le_header", "Skyrim LE 형식(HEDR 0.94) 플러그인입니다. 헤더와 레코드 형식은 그대로 두고 문자열만 바꿉니다."));
        var withTrailingBytes = records.Values.Count(record => record.TrailingCompressedBytes > 0);
        if (withTrailingBytes > 0)
            diagnostics.Add(new("compressed_trailing_bytes", $"압축 레코드 {withTrailingBytes}개의 끝에 쓰이지 않는 바이트가 붙어 있습니다. 원본 그대로 두고 읽었습니다(번역해 저장하는 레코드는 다시 압축되어 그 바이트가 빠집니다)."));
        var identities = new Dictionary<(string, uint), int>();
        foreach (var record in records.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.Index == 0 || (record.Flags & PluginBinary.DeletedFlag) != 0) continue;
            if (!PluginFieldRegistry.IsKnownRecord(record.Type) && unknown.Add(record.Type))
                diagnostics.Add(new("unknown_record", $"지원 정의에 없는 레코드 {record.Type}가 있습니다. 원본 데이터는 보존되지만 전체 번역 여부를 확인할 수 없어 저장을 중단합니다.", true));
            var recordKey = (record.Type, record.FormId);
            var recordOccurrence = identities.GetValueOrDefault(recordKey);
            identities[recordKey] = recordOccurrence + 1;
            var edidSub = record.Subrecords.FirstOrDefault(sub => sub.Type == "EDID");
            var edid = edidSub == null ? null : PluginBinary.ReadZString(edidSub.Data.Span, metadataEncoding, "EDID");
            var preceding = new List<PluginSubrecordDescriptor>();
            var subOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var sub in record.Subrecords)
            {
                var ordinal = subOccurrences.GetValueOrDefault(sub.Type);
                subOccurrences[sub.Type] = ordinal + 1;
                var rule = PluginFieldRegistry.GetRule(record.Type, sub.Type, edid, preceding);
                if (rule != null)
                {
                    string source;
                    PluginStringTableKind? kind = localized ? rule.LocalizedTable : null;
                    uint? stringId = null;
                    if (kind != null)
                    {
                        if (sub.Data.Length != 4) throw new InvalidDataException($"localized 필드의 ID 길이가 잘못되었습니다: {record.Type}:{sub.Type}/{record.FormId:X8}");
                        stringId = PluginBinary.U32(sub.Data.Span, 0);
                        if (stringId == 0) { preceding.Add(new(sub.Type, sub.Data)); continue; }
                        if (!tables.TryGetValue(kind.Value, out var table))
                            throw new FileNotFoundException($"{Path.GetFileName(path)}에 필요한 {Extension(kind.Value)} 원문 테이블이 없습니다. Strings 폴더 또는 BSA 경로를 확인하세요.");
                        if (!table.Strings.TryGetValue(stringId.Value, out source!))
                            throw new InvalidDataException($"{kind} 테이블에서 StringID {stringId}를 찾을 수 없습니다.");
                    }
                    // A subrecord with no bytes at all (not even the terminator) is an empty field; some mods ship them.
                    else source = sub.Data.Length == 0 ? "" : PluginBinary.ReadZString(sub.Data.Span, encoding, $"{record.Type}:{sub.Type}/{record.FormId:X8}");
                    // Empty optional fields do not create artificial translation work.
                    if (source.Length > 0)
                        fields.Add(new($"{record.Type}/{record.FormId:X8}/{recordOccurrence}/{sub.Type}/{ordinal}", fields.Count,
                            record.Type, sub.Type, record.FormId, edid, record.Index, sub.Index, source, kind, stringId, rule.RequiresNonEmpty,
                            record.DialogueTopicFormId));
                }
                // SNDR:FNAM (form version < 35) is a flags field (bit 4 = Loop), not a string ID, even
                // though some readers model it as translated text. It is preserved without a warning.
                if (rule == null && record.Type == "QUST" && sub.Type == "NNAM")
                {
                    var preservation = localized
                        ? "필드 바이트와 ID는 유지하지만, 다른 번역 항목과 문자열 ID를 공유하면 참조 내용은 함께 바뀔 수 있습니다."
                        : "해당 필드의 바이트를 유지합니다.";
                    diagnostics.Add(new("ambiguous_field", $"{record.Type}:{sub.Type}/{record.FormId:X8}는 도구별 필드 정의가 달라 직접 번역하지 않습니다. {preservation}"));
                }
                preceding.Add(new(sub.Type, sub.Data));
            }
        }
        options = options with { ArchivePaths = options.ArchivePaths?.ToArray() };
        return new(new(path, PluginBinary.Hash(bytes), options, localized, Array.AsReadOnly(masters), diagnostics.AsReadOnly(), tableSources),
            fields.AsReadOnly(), bytes, nodes, records, tables,
            dependencies ?? new Dictionary<string, string>());
    }

    /// <summary>
    /// Skyrim LE plugins keep HEDR 0.94 and the SE game loads them. Fallout 3 and New Vegas use 0.94 too, so it counts
    /// only with Skyrim.esm among the masters or a Skyrim form version (40-44; Fallout uses 15) on the header record.
    /// </summary>
    private static bool IsSkyrimLeHeader(float version, IReadOnlyList<string> masters, ReadOnlySpan<byte> headerRecord)
    {
        if (!(Math.Abs(version - 0.94f) <= 0.0001f)) return false; // also false for NaN
        if (masters.Any(master => string.Equals(master, "Skyrim.esm", StringComparison.OrdinalIgnoreCase))) return true;
        var formVersion = headerRecord.Length >= 22 ? BitConverter.ToUInt16(headerRecord.Slice(20, 2)) : 0;
        return formVersion is >= 40 and <= 44;
    }

    internal static (IReadOnlyList<PluginNode> Nodes, IReadOnlyDictionary<int, PluginRecord> Records) ParseStructure(byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length < PluginBinary.HeaderSize || PluginBinary.Signature(bytes) != "TES4")
            throw new InvalidDataException("TES4 헤더가 있는 Bethesda 플러그인이 아닙니다.");
        var records = new Dictionary<int, PluginRecord>();
        var formIds = new HashSet<uint>();
        var nextIndex = 0;
        var nodes = ParseRange(0, bytes.Length, 0, null);
        if (nodes[0] is not PluginRecord { Type: "TES4" }) throw new InvalidDataException("TES4 헤더가 첫 레코드가 아닙니다.");
        return (nodes, records);

        IReadOnlyList<PluginNode> ParseRange(int start, int end, int depth, uint? dialogueTopicFormId)
        {
            if (depth > 64) throw new InvalidDataException("GRUP 중첩 깊이가 지원 범위를 초과했습니다.");
            var result = new List<PluginNode>();
            var pos = start;
            while (pos < end)
            {
                ct.ThrowIfCancellationRequested();
                if (end - pos < PluginBinary.HeaderSize) throw new InvalidDataException($"잘린 레코드/GRUP 헤더: {pos}");
                var type = PluginBinary.Signature(bytes.AsSpan(pos));
                var size = PluginBinary.U32(bytes, pos + 4);
                var total = type == "GRUP" ? (long)size : PluginBinary.HeaderSize + (long)size;
                if (total < PluginBinary.HeaderSize || total > end - pos) throw new InvalidDataException($"{type} 길이가 부모 경계를 벗어납니다: {pos}");
                var raw = bytes.AsMemory(pos, (int)total);
                if (type == "GRUP")
                {
                    // Topic Children (type 7) stores the owning DIAL FormID in its label.
                    // An override-only patch need not contain the parent DIAL record itself.
                    var groupType = PluginBinary.U32(raw.Span, 12);
                    var label = PluginBinary.U32(raw.Span, 8);
                    uint? childTopic = groupType == 7 ? (label == 0 ? null : label) : dialogueTopicFormId;
                    result.Add(new PluginGroup(raw, ParseRange(pos + PluginBinary.HeaderSize, pos + (int)total, depth + 1, childTopic)));
                }
                else
                {
                    if (type == "TES4" && nextIndex != 0) throw new InvalidDataException("중복 TES4 헤더입니다.");
                    var flags = PluginBinary.U32(raw.Span, 8);
                    var formId = PluginBinary.U32(raw.Span, 12);
                    if (type != "TES4" && formId != 0 && !formIds.Add(formId))
                        throw new InvalidDataException($"중복 FormID {formId:X8}가 있습니다: {type} (offset {pos})");
                    var trailing = 0;
                    ReadOnlyMemory<byte> payload = (flags & PluginBinary.CompressedFlag) != 0
                        ? PluginBinary.Inflate(raw[PluginBinary.HeaderSize..], allowTrailingBytes: true, out trailing)
                        : raw[PluginBinary.HeaderSize..];
                    IReadOnlyList<PluginSubrecord> subrecords;
                    try { subrecords = ParseSubrecords(payload); }
                    catch (Exception ex) when (ex is InvalidDataException or OverflowException)
                    { throw new InvalidDataException($"{type}/{formId:X8} (offset {pos}) subrecord 해석 실패: {ex.Message}", ex); }
                    var topic = type == "DIAL" ? (formId == 0 ? (uint?)null : formId)
                        : type == "INFO" ? dialogueTopicFormId : null;
                    var record = new PluginRecord(raw, nextIndex++, type, formId, flags, payload, subrecords, topic, trailing);
                    records.Add(record.Index, record);
                    result.Add(record);
                }
                pos += (int)total;
            }
            return result.AsReadOnly();
        }
    }

    private static IReadOnlyList<PluginSubrecord> ParseSubrecords(ReadOnlyMemory<byte> payload)
    {
        var result = new List<PluginSubrecord>();
        var offset = 0;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < 6) throw new InvalidDataException("잘린 subrecord 헤더입니다.");
            string type;
            try { type = PluginBinary.Signature(payload.Span[offset..], allowBinary: true); }
            catch (InvalidDataException ex) { throw new InvalidDataException($"subrecord offset {offset}, signature {Convert.ToHexString(payload.Span.Slice(offset, 4))}: {ex.Message}", ex); }
            var length = (uint)PluginBinary.U16(payload.Span, offset + 4);
            var headerLength = 6;
            if (type == "XXXX")
            {
                if (length != 4 || payload.Length - offset < 16) throw new InvalidDataException("잘못된 XXXX 확장 길이입니다.");
                length = PluginBinary.U32(payload.Span, offset + 6);
                type = PluginBinary.Signature(payload.Span[(offset + 10)..], allowBinary: true);
                if (type == "XXXX") throw new InvalidDataException("중복 XXXX 헤더입니다.");
                headerLength = 16;
            }
            if ((long)offset + headerLength + length > payload.Length) throw new InvalidDataException($"{type} subrecord 길이가 레코드 경계를 벗어납니다.");
            result.Add(new(type, result.Count, offset, headerLength, payload.Slice(offset + headerLength, (int)length)));
            offset = checked(offset + headerLength + (int)length);
        }
        return result.AsReadOnly();
    }

    internal static string Extension(PluginStringTableKind kind) => kind switch
    { PluginStringTableKind.Strings => "STRINGS", PluginStringTableKind.DlStrings => "DLSTRINGS", PluginStringTableKind.IlStrings => "ILSTRINGS", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    internal static string ValidateLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Any(ch => ch is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z')))
            throw new ArgumentException("언어 파일명은 english, korean 같은 영문 이름이어야 합니다.");
        return language.ToLowerInvariant();
    }
    private static string? FindFile(string folder, string name)
        => Directory.Exists(folder) ? Directory.EnumerateFiles(folder).FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)) : null;
    private static IEnumerable<string> ResolveArchives(string path, PluginReadOptions options)
    {
        if (options.ArchivePaths != null) return options.ArchivePaths.Select(Path.GetFullPath).ToArray();
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        return Directory.EnumerateFiles(dir, "*.bsa")
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), stem, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(file).StartsWith(stem + " - ", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Tables are looked up by the plugin's own file name, so another plugin's tables are never taken from it.
    private static string? FindGameInterfaceArchive(string path, PluginReadOptions options, IReadOnlyCollection<string> archives)
    {
        if (options.ArchivePaths != null) return null;
        var found = FindFile(Path.GetDirectoryName(path)!, GameInterfaceArchive);
        return found == null || archives.Contains(found, StringComparer.OrdinalIgnoreCase) ? null : found;
    }

    private const string GameInterfaceArchive = "Skyrim - Interface.bsa";
}

internal static class PluginFlags
{
    internal static bool HasFlag(this uint value, uint flag) => (value & flag) != 0;
}
