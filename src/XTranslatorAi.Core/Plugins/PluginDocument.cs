using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Plugins;

/// <summary>An immutable input snapshot. All offsets belong to this snapshot, not to a later file revision.</summary>
public sealed class PluginDocument
{
    public PluginSourceInfo Info { get; }
    public IReadOnlyList<PluginField> Fields { get; }
    internal byte[] Bytes { get; }
    internal IReadOnlyList<PluginNode> Nodes { get; }
    internal IReadOnlyDictionary<int, PluginRecord> Records { get; }
    internal IReadOnlyDictionary<PluginStringTableKind, PluginStringTable> Tables { get; }
    internal IReadOnlyDictionary<string, string> DependencyHashes { get; }

    internal PluginDocument(PluginSourceInfo info, IReadOnlyList<PluginField> fields, byte[] bytes,
        IReadOnlyList<PluginNode> nodes, IReadOnlyDictionary<int, PluginRecord> records,
        IReadOnlyDictionary<PluginStringTableKind, PluginStringTable> tables,
        IReadOnlyDictionary<string, string> dependencyHashes)
    {
        Info = info; Fields = fields; Bytes = bytes; Nodes = nodes;
        Records = records; Tables = tables; DependencyHashes = dependencyHashes;
    }
}

internal abstract record PluginNode(ReadOnlyMemory<byte> Raw);
internal sealed record PluginGroup(ReadOnlyMemory<byte> Raw, IReadOnlyList<PluginNode> Children) : PluginNode(Raw);
internal sealed record PluginRecord(ReadOnlyMemory<byte> Raw, int Index, string Type, uint FormId,
    uint Flags, ReadOnlyMemory<byte> Payload, IReadOnlyList<PluginSubrecord> Subrecords,
    uint? DialogueTopicFormId = null) : PluginNode(Raw);
internal sealed record PluginSubrecord(string Type, int Index, int Offset, int HeaderLength, ReadOnlyMemory<byte> Data)
{
    public int Length => HeaderLength + Data.Length;
}
