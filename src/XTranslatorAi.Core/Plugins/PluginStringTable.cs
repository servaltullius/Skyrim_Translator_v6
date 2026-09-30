using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace XTranslatorAi.Core.Plugins;

internal sealed class PluginStringTable
{
    internal PluginStringTableKind Kind { get; }
    internal byte[] Bytes { get; }
    internal IReadOnlyDictionary<uint, string> Strings { get; }
    private readonly List<(uint Id, byte[] Raw)> _entries;

    private PluginStringTable(PluginStringTableKind kind, byte[] bytes, Dictionary<uint, string> strings, List<(uint Id, byte[] Raw)> entries)
    { Kind = kind; Bytes = bytes; Strings = strings; _entries = entries; }

    internal static PluginStringTable Read(PluginStringTableKind kind, byte[] bytes, Encoding encoding)
    {
        if (bytes.Length < 8) throw new InvalidDataException($"잘린 {kind} 테이블입니다.");
        var count = PluginBinary.U32(bytes, 0);
        var dataSize = PluginBinary.U32(bytes, 4);
        var dataStart = 8L + count * 8L;
        if (dataStart + dataSize != bytes.LongLength) throw new InvalidDataException($"{kind} 테이블의 크기/항목 수가 일치하지 않습니다.");
        var strings = new Dictionary<uint, string>();
        var entries = new List<(uint, byte[])>();
        for (var i = 0; i < count; i++)
        {
            var entryOffset = checked(8 + (int)i * 8);
            var id = PluginBinary.U32(bytes, entryOffset);
            var offset = PluginBinary.U32(bytes, entryOffset + 4);
            if (offset >= dataSize) throw new InvalidDataException($"{kind} ID {id}의 offset 범위가 잘못되었습니다.");
            var start = checked((int)(dataStart + offset));
            int rawLength;
            ReadOnlySpan<byte> text;
            if (kind == PluginStringTableKind.Strings)
            {
                var end = bytes.AsSpan(start).IndexOf((byte)0);
                if (end < 0) throw new InvalidDataException($"{kind} ID {id}에 종료 문자가 없습니다.");
                rawLength = end + 1;
                text = bytes.AsSpan(start, rawLength);
            }
            else
            {
                if (bytes.Length - start < 4) throw new InvalidDataException($"{kind} ID {id}의 길이가 잘렸습니다.");
                var length = PluginBinary.U32(bytes, start);
                if (length == 0 || (long)start + 4 + length > bytes.Length)
                    throw new InvalidDataException($"{kind} ID {id}의 길이가 잘못되었습니다.");
                rawLength = checked(4 + (int)length);
                text = bytes.AsSpan(start + 4, (int)length);
            }
            if (!strings.TryAdd(id, PluginBinary.ReadZString(text, encoding, $"{kind}:{id}")))
                throw new InvalidDataException($"{kind} 테이블에 중복 ID {id}가 있습니다.");
            entries.Add((id, bytes.AsSpan(start, rawLength).ToArray()));
        }
        return new(kind, bytes, strings, entries);
    }

    internal byte[] Write(IReadOnlyDictionary<uint, string> replacements, Encoding encoding)
    {
        if (replacements.Count == 0) return Bytes;
        if (replacements.Keys.Any(id => !Strings.ContainsKey(id))) throw new InvalidDataException("테이블에 없는 StringID입니다.");
        using var output = new MemoryStream();
        var directory = new byte[checked(8 + _entries.Count * 8)];
        PluginBinary.SetU32(directory, 0, checked((uint)_entries.Count));
        output.Write(directory);
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            PluginBinary.SetU32(directory, 8 + i * 8, entry.Id);
            PluginBinary.SetU32(directory, 12 + i * 8, checked((uint)(output.Position - directory.Length)));
            if (replacements.TryGetValue(entry.Id, out var translated))
            {
                var bytes = PluginBinary.EncodeZString(translated, encoding);
                if (Kind != PluginStringTableKind.Strings)
                {
                    var length = new byte[4];
                    PluginBinary.SetU32(length, 0, checked((uint)bytes.Length));
                    output.Write(length);
                }
                output.Write(bytes);
            }
            else output.Write(entry.Raw);
        }
        PluginBinary.SetU32(directory, 4, checked((uint)(output.Length - directory.Length)));
        output.Position = 0;
        output.Write(directory);
        return output.ToArray();
    }
}
