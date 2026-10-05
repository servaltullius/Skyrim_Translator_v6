using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Plugins;

public static class PluginWriter
{
    /// <summary>Publishes a complete new folder. Never replaces an input plugin or a partly written output set.</summary>
    public static async Task<PluginExportResult> ExportAsync(PluginDocument document,
        IReadOnlyDictionary<string, string> translations, PluginExportOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var output = Path.GetFullPath(options.OutputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(parent) || Directory.Exists(output) || File.Exists(output))
            throw new IOException("출력은 아직 존재하지 않는 새 폴더를 선택하세요. 기존 폴더나 원본을 덮어쓰지 않습니다.");
        var blocked = document.Info.Diagnostics.Where(d => d.BlocksExport).ToArray();
        if (blocked.Length > 0) throw new NotSupportedException(string.Join("\n", blocked.Select(d => d.Message)));
        var edits = translations.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var encoding = PluginBinary.GetEncoding(options.TargetEncoding);
        var outputLanguage = PluginReader.ValidateLanguage(options.OutputLanguage ?? document.Info.Options.SourceLanguage);
        var fields = document.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        foreach (var (key, translated) in edits)
        {
            if (!fields.TryGetValue(key, out var field)) throw new InvalidDataException($"원본에 없는 번역 필드: {key}");
            if (translated == null || (field.RequiresNonEmpty && string.IsNullOrWhiteSpace(translated)))
                throw new InvalidDataException($"비어 있는 번역을 저장할 수 없습니다: {field.Rec}/{field.FormId:X8}");
            TokenValidator.ValidateFinalTextIntegrity(field.SourceText, translated, $"plugin:{key}");
        }

        // Strict representability check before any output, for every text the output will hold: a kept source
        // ("Café", an em dash) fails too when the output encoding differs. E457 used to say only that some character
        // somewhere could not be written.
        foreach (var field in document.Fields)
        {
            var text = edits.GetValueOrDefault(field.Key, field.SourceText) ?? "";
            try
            {
                _ = PluginBinary.EncodeZString(text, encoding);
            }
            catch (EncoderFallbackException ex)
            {
                var ch = ex.CharUnknown != '\0' ? ex.CharUnknown : ex.CharUnknownHigh;
                throw new InvalidDataException($"출력 인코딩으로 표현할 수 없는 문자가 있습니다: {field.Rec}/{field.FormId:X8} U+{(int)ch:X4}", ex);
            }
        }

        // A changed encoding rewrites whole tables, so their strings no field uses are converted too. One that does
        // not fit failed later with no StringID, and no row in the project can change it.
        if (PluginBinary.GetEncoding(document.Info.Options.SourceEncoding).CodePage != encoding.CodePage)
        {
            foreach (var (kind, table) in document.Tables)
            {
                var used = document.Fields.Where(field => field.TableKind == kind).Select(field => field.StringId!.Value).ToHashSet();
                foreach (var (id, text) in table.Strings)
                {
                    if (used.Contains(id)) continue;
                    try
                    {
                        _ = PluginBinary.EncodeZString(text, encoding);
                    }
                    catch (EncoderFallbackException ex)
                    {
                        var ch = ex.CharUnknown != '\0' ? ex.CharUnknown : ex.CharUnknownHigh;
                        throw new InvalidDataException(
                            $"쓰이지 않는 문자열에 출력 인코딩으로 표현할 수 없는 문자가 있습니다: {kind.ToString().ToUpperInvariant()}/{id} U+{(int)ch:X4}", ex);
                    }
                }
            }
        }
        var inputHandles = new List<FileStream>();
        string? temporary = null;
        try
        {
            // Keep input files read-locked from revalidation through publication.
            await LockInputAsync(document.Info.InputPath, document.Info.Sha256);
            foreach (var dependency in document.DependencyHashes) await LockInputAsync(dependency.Key, dependency.Value);
            cancellationToken.ThrowIfCancellationRequested();
            var (pluginBytes, tableBytes, changed) = BuildOutput(document, edits, encoding, cancellationToken);
            VerifyOutput(document, pluginBytes, tableBytes, edits, options.TargetEncoding, cancellationToken);
            Directory.CreateDirectory(parent);
            temporary = Path.Combine(parent, "." + Path.GetFileName(output) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            Directory.CreateDirectory(temporary);
            var name = Path.GetFileName(document.Info.InputPath);
            var files = new List<string> { Path.Combine(output, name) };
            await File.WriteAllBytesAsync(Path.Combine(temporary, name), pluginBytes, cancellationToken).ConfigureAwait(false);
            if (tableBytes.Count > 0)
            {
                Directory.CreateDirectory(Path.Combine(temporary, "Strings"));
                foreach (var (kind, data) in tableBytes)
                {
                    var tableName = Path.GetFileNameWithoutExtension(name) + "_" + outputLanguage + "." + PluginReader.Extension(kind);
                    await File.WriteAllBytesAsync(Path.Combine(temporary, "Strings", tableName), data, cancellationToken).ConfigureAwait(false);
                    files.Add(Path.Combine(output, "Strings", tableName));
                }
            }
            // Verify the written bytes, not only the buffers used to produce them.
            var diskPlugin = await File.ReadAllBytesAsync(Path.Combine(temporary, name), cancellationToken).ConfigureAwait(false);
            if (!diskPlugin.AsSpan().SequenceEqual(pluginBytes)) throw new IOException("출력 플러그인 디스크 검증에 실패했습니다.");
            foreach (var (kind, data) in tableBytes)
            {
                var tableName = Path.GetFileNameWithoutExtension(name) + "_" + outputLanguage + "." + PluginReader.Extension(kind);
                var disk = await File.ReadAllBytesAsync(Path.Combine(temporary, "Strings", tableName), cancellationToken).ConfigureAwait(false);
                if (!disk.AsSpan().SequenceEqual(data)) throw new IOException("출력 문자열 테이블 디스크 검증에 실패했습니다.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(temporary, output); // Same-parent directory rename publishes the complete set.
            temporary = null;
            return new(Path.Combine(output, name), files.AsReadOnly(), changed, PluginBinary.Hash(pluginBytes));
        }
        finally
        {
            foreach (var handle in inputHandles) await handle.DisposeAsync();
            if (temporary != null && Directory.Exists(temporary))
            {
                var resolved = Path.GetFullPath(temporary);
                if (string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(resolved).EndsWith(".tmp", StringComparison.Ordinal)) Directory.Delete(resolved, recursive: true);
            }
        }

        async Task LockInputAsync(string path, string expectedHash)
        {
            var handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            inputHandles.Add(handle);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(handle, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
                throw new InvalidDataException($"불러온 뒤 원본이 변경되었습니다. 다시 열어주세요: {Path.GetFileName(path)}");
        }
    }

    internal static (byte[] Plugin, Dictionary<PluginStringTableKind, byte[]> Tables, int Changed) BuildOutput(
        PluginDocument doc, IReadOnlyDictionary<string, string> edits, Encoding encoding, CancellationToken ct)
    {
        var transcode = PluginBinary.GetEncoding(doc.Info.Options.SourceEncoding).CodePage != encoding.CodePage;
        var fieldByKey = doc.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var changedCount = edits.Count(pair => pair.Value != fieldByKey[pair.Key].SourceText);
        var inline = new Dictionary<(int, int), byte[]>();
        var tableEdits = new Dictionary<PluginStringTableKind, Dictionary<uint, string>>();
        foreach (var group in doc.Fields.Where(field => field.TableKind != null).GroupBy(field => (field.TableKind!.Value, field.StringId!.Value)))
        {
            var requested = group.Select(field => edits.GetValueOrDefault(field.Key, field.SourceText)).Distinct(StringComparer.Ordinal).ToArray();
            if (requested.Length != 1)
            {
                var selector = $"string:{group.Key.Item1.ToString().ToUpperInvariant()}/{group.Key.Item2}";
                var rows = string.Join(", ", group.Take(12).Select(field => $"#{field.OrderIndex}"));
                throw new InvalidDataException($"여러 필드가 같은 {group.Key.Item1} StringID {group.Key.Item2}를 공유하지만 번역이 다릅니다. "
                    + $"Search에 {selector} 입력 후 Status를 All로 설정하고 공유 행의 번역을 일치시켜 주세요. 관련 행: {rows}");
            }
            if (transcode || requested[0] != group.First().SourceText)
            {
                if (!tableEdits.TryGetValue(group.Key.Item1, out var changes)) tableEdits[group.Key.Item1] = changes = new();
                changes[group.Key.Item2] = requested[0];
            }
        }
        foreach (var field in doc.Fields.Where(field => field.TableKind == null))
        {
            var translated = edits.GetValueOrDefault(field.Key, field.SourceText);
            if (transcode || translated != field.SourceText)
                inline[(field.RecordIndex, field.SubrecordIndex)] = PluginBinary.EncodeZString(translated, encoding);
        }
        var tables = new Dictionary<PluginStringTableKind, byte[]>();
        foreach (var (kind, table) in doc.Tables)
        {
            var replacements = tableEdits.GetValueOrDefault(kind) ?? new();
            if (transcode)
                foreach (var (id, source) in table.Strings) replacements.TryAdd(id, source);
            tables[kind] = table.Write(replacements, encoding);
        }
        if (inline.Count == 0) return (doc.Bytes, tables, changedCount);
        using var output = new MemoryStream();
        foreach (var node in doc.Nodes) WriteNode(output, node);
        return (output.ToArray(), tables, changedCount);

        bool HasEdits(PluginNode node) => node switch
        {
            PluginRecord record => record.Subrecords.Any(sub => inline.ContainsKey((record.Index, sub.Index))),
            PluginGroup group => group.Children.Any(HasEdits),
            _ => false,
        };
        void WriteNode(Stream target, PluginNode node)
        {
            ct.ThrowIfCancellationRequested();
            if (!HasEdits(node)) { target.Write(node.Raw.Span); return; }
            var header = node.Raw[..PluginBinary.HeaderSize].ToArray();
            if (node is PluginGroup group)
            {
                var position = target.Position;
                target.Write(header);
                foreach (var child in group.Children) WriteNode(target, child);
                var end = target.Position;
                PluginBinary.SetU32(header, 4, checked((uint)(end - position)));
                target.Position = position;
                target.Write(header);
                target.Position = end;
            }
            else if (node is PluginRecord record)
            {
                using var payload = new MemoryStream();
                foreach (var sub in record.Subrecords)
                {
                    if (!inline.TryGetValue((record.Index, sub.Index), out var data))
                        payload.Write(record.Payload.Span.Slice(sub.Offset, sub.Length));
                    else WriteSubrecord(payload, sub.Type, data, sub.HeaderLength == 16);
                }
                var bytes = payload.ToArray();
                if ((record.Flags & PluginBinary.CompressedFlag) != 0) bytes = PluginBinary.Deflate(bytes);
                PluginBinary.SetU32(header, 4, checked((uint)bytes.Length));
                target.Write(header);
                target.Write(bytes);
            }
        }
    }

    private static void WriteSubrecord(Stream stream, string type, byte[] data, bool preserveExtendedHeader)
    {
        var extended = preserveExtendedHeader || data.Length > ushort.MaxValue;
        var header = new byte[extended ? 16 : 6];
        var index = 0;
        if (extended)
        {
            Encoding.ASCII.GetBytes("XXXX", header);
            PluginBinary.SetU16(header, 4, 4);
            PluginBinary.SetU32(header, 6, checked((uint)data.Length));
            index = 10;
        }
        Encoding.ASCII.GetBytes(type, header.AsSpan(index));
        PluginBinary.SetU16(header, index + 4, extended ? (ushort)0 : checked((ushort)data.Length));
        stream.Write(header); stream.Write(data);
    }

    private static void VerifyOutput(PluginDocument original, byte[] bytes, Dictionary<PluginStringTableKind, byte[]> tables,
        IReadOnlyDictionary<string, string> edits, string encoding, CancellationToken ct)
    {
        var parsed = PluginReader.ReadSnapshot(original.Info.InputPath, bytes, original.Info.Options with { SourceEncoding = encoding }, tables, cancellationToken: ct);
        if (parsed.Fields.Count != original.Fields.Count || parsed.Records.Count != original.Records.Count)
            throw new InvalidDataException("저장 검증 중 레코드/번역 필드 수가 변경되었습니다.");
        var allowed = original.Fields.Where(f => f.TableKind == null).ToDictionary(f => (f.RecordIndex, f.SubrecordIndex));
        var transcode = PluginBinary.GetEncoding(original.Info.Options.SourceEncoding).CodePage != PluginBinary.GetEncoding(encoding).CodePage;
        var changedRecords = allowed.Values.Where(field => transcode || edits.GetValueOrDefault(field.Key, field.SourceText) != field.SourceText)
            .Select(field => field.RecordIndex).ToHashSet();
        foreach (var record in original.Records.Values)
        {
            ct.ThrowIfCancellationRequested();
            var other = parsed.Records[record.Index];
            if (!changedRecords.Contains(record.Index) && !record.Raw.Span.SequenceEqual(other.Raw.Span))
                throw new InvalidDataException($"수정하지 않은 레코드의 원본 바이트가 변경되었습니다: {record.Type}/{record.FormId:X8}");
            if (!record.Raw.Span[..4].SequenceEqual(other.Raw.Span[..4])
                || !record.Raw.Span.Slice(8, 16).SequenceEqual(other.Raw.Span.Slice(8, 16))
                || record.Subrecords.Count != other.Subrecords.Count)
                throw new InvalidDataException("번역 이외 레코드 헤더/구조가 변경되었습니다.");
            for (var i = 0; i < record.Subrecords.Count; i++)
            {
                var before = record.Subrecords[i]; var after = other.Subrecords[i];
                if (before.Type != after.Type) throw new InvalidDataException("subrecord 순서가 변경되었습니다.");
                if (!allowed.ContainsKey((record.Index, i)) && !record.Payload.Span.Slice(before.Offset, before.Length)
                    .SequenceEqual(other.Payload.Span.Slice(after.Offset, after.Length)))
                    throw new InvalidDataException($"비번역 데이터가 변경되었습니다: {record.Type}:{before.Type}/{record.FormId:X8}");
            }
        }
        CompareGroups(original.Nodes, parsed.Nodes);
        for (var i = 0; i < original.Fields.Count; i++)
        {
            var field = original.Fields[i]; var actual = parsed.Fields[i];
            var expected = edits.GetValueOrDefault(field.Key, field.SourceText);
            if (field.Key != actual.Key || expected != actual.SourceText || field.StringId != actual.StringId || field.TableKind != actual.TableKind
                || field.DialogueTopicFormId != actual.DialogueTopicFormId)
                throw new InvalidDataException($"번역 필드 저장 검증에 실패했습니다: {field.Key}");
        }
        foreach (var (kind, table) in original.Tables)
        {
            var output = parsed.Tables[kind];
            if (!table.Strings.Keys.Order().SequenceEqual(output.Strings.Keys.Order())) throw new InvalidDataException("StringID 집합이 변경되었습니다.");
            var references = original.Fields.Where(f => f.TableKind == kind).GroupBy(f => f.StringId!.Value).ToDictionary(g => g.Key, g => g.First());
            foreach (var (id, text) in table.Strings)
            {
                var expected = references.TryGetValue(id, out var reference) ? edits.GetValueOrDefault(reference.Key, text) : text;
                if (output.Strings[id] != expected) throw new InvalidDataException($"StringID {id}의 저장 결과가 일치하지 않습니다.");
            }
        }

        static void CompareGroups(IReadOnlyList<PluginNode> before, IReadOnlyList<PluginNode> after)
        {
            if (before.Count != after.Count) throw new InvalidDataException("GRUP 노드 수가 변경되었습니다.");
            for (var i = 0; i < before.Count; i++)
            {
                if (before[i].GetType() != after[i].GetType()) throw new InvalidDataException("GRUP 레코드 순서가 변경되었습니다.");
                if (before[i] is PluginGroup group && after[i] is PluginGroup other)
                {
                    if (!group.Raw.Span.Slice(8, 16).SequenceEqual(other.Raw.Span.Slice(8, 16))) throw new InvalidDataException("GRUP 식별 정보가 변경되었습니다.");
                    CompareGroups(group.Children, other.Children);
                }
            }
        }
    }
}
