using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// What the base game masters say about who speaks a mod's lines, for the speakers a plugin alone cannot name (a
/// <see cref="SpeakerRef.Hint"/>): the one NPC that uses a voice type, the NPC a placed actor is, and the NPC a master
/// quest's alias holds. Serana Dialogue Add-On names Serana by her voice type and by aliases filled with Dawnguard's
/// Serana, so all but a tenth of its 8,600 lines had no speaker. Records are named by the master that defines them
/// ("dawnguard.esm:002b6c"), as <see cref="GameStringDictionary.FieldIdentity"/> names fields, and the index travels
/// in the game translation dictionary.
/// </summary>
public sealed class MasterSpeakerIndex
{
    private const string PlayerValue = "player";

    private readonly Dictionary<string, string> _voices;
    private readonly Dictionary<string, string> _refs;
    private readonly Dictionary<string, string> _aliases;

    private MasterSpeakerIndex(Dictionary<string, string> voices, Dictionary<string, string> refs, Dictionary<string, string> aliases)
    {
        _voices = voices;
        _refs = refs;
        _aliases = aliases;
    }

    public int Count => _voices.Count + _refs.Count + _aliases.Count;

    /// <summary>
    /// The fields with the speakers and listeners their hints name filled in; a hint the masters cannot resolve leaves
    /// the field as it was (a listener stays "another character").
    /// </summary>
    public IReadOnlyList<PluginField> Resolve(IReadOnlyList<PluginField> fields, IReadOnlyList<string> masters, string pluginFileName)
    {
        List<PluginField>? resolved = null;
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (field.SpeakerHint == null && field.ListenerHint == null) continue;
            var speaker = field.SpeakerFormId;
            if (speaker == null && Lookup(field.SpeakerHint, masters, pluginFileName) is { } s && s != PlayerValue)
            {
                speaker = ToFormId(s, masters);
            }

            var listener = field.ListenerFormId;
            if (speaker != null && Lookup(field.ListenerHint, masters, pluginFileName) is { } l)
            {
                listener = l == PlayerValue ? null : ToFormId(l, masters) ?? listener;
                if (listener == speaker) listener = null;
            }

            if (speaker == field.SpeakerFormId && listener == field.ListenerFormId) continue;
            resolved ??= fields.ToList();
            resolved[i] = field with { SpeakerFormId = speaker, ListenerFormId = speaker == null ? field.ListenerFormId : listener };
        }

        return resolved ?? fields;
    }

    private string? Lookup(string? hint, IReadOnlyList<string> masters, string pluginFileName)
    {
        if (hint == null || hint.Length < 10 || hint[1] != ':'
            || !uint.TryParse(hint.AsSpan(2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var formId))
        {
            return null;
        }

        var identity = Identity(formId, masters, pluginFileName);
        return hint[0] switch
        {
            'V' => _voices.GetValueOrDefault(identity),
            'R' => _refs.GetValueOrDefault(identity),
            'A' when hint.Length > 11 && hint[10] == ':' => _aliases.GetValueOrDefault(identity + hint[10..]),
            _ => null,
        };
    }

    /// <summary>A record named by the plugin that defines it: "dawnguard.esm:002b6c".</summary>
    internal static string Identity(uint formId, IReadOnlyList<string> masters, string fileName)
    {
        var index = (int)(formId >> 24);
        var owner = index < masters.Count ? masters[index] : fileName;
        return string.Create(CultureInfo.InvariantCulture, $"{owner.ToLowerInvariant()}:{formId & 0x00FFFFFFu:x6}");
    }

    private static uint? ToFormId(string identity, IReadOnlyList<string> masters)
    {
        var colon = identity.LastIndexOf(':');
        if (colon < 0 || !uint.TryParse(identity.AsSpan(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var local))
        {
            return null;
        }

        var owner = identity[..colon];
        for (var i = 0; i < masters.Count; i++)
        {
            if (string.Equals(masters[i], owner, StringComparison.OrdinalIgnoreCase)) return ((uint)i << 24) | local;
        }

        return null;
    }

    internal IEnumerable<string> Lines()
    {
        foreach (var (voice, npc) in _voices) yield return $"SV\t{voice}\t{npc}";
        foreach (var (reference, npc) in _refs) yield return $"SR\t{reference}\t{npc}";
        foreach (var (alias, npc) in _aliases) yield return $"SA\t{alias}\t{npc}";
    }

    /// <summary>Reads a line Lines() wrote; false for any other line.</summary>
    internal static bool TryReadLine(string[] parts, Dictionary<string, string> voices, Dictionary<string, string> refs,
        Dictionary<string, string> aliases)
    {
        if (parts.Length != 3) return false;
        var map = parts[0] switch { "SV" => voices, "SR" => refs, "SA" => aliases, _ => null };
        if (map == null) return false;
        map[parts[1]] = parts[2];
        return true;
    }

    internal static MasterSpeakerIndex? FromMaps(Dictionary<string, string> voices, Dictionary<string, string> refs,
        Dictionary<string, string> aliases)
        => voices.Count + refs.Count + aliases.Count == 0 ? null : new MasterSpeakerIndex(voices, refs, aliases);

    /// <summary>Gathers the masters in load order (a later master's override replaces the earlier record).</summary>
    public sealed class Builder
    {
        private const uint UniqueFlag = 0x20;

        private readonly Dictionary<string, string> _npcNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _npcVoices = new(StringComparer.Ordinal);
        private readonly HashSet<string> _uniqueNpcs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _refs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);

        public void Add(PluginDocument master)
        {
            var fileName = Path.GetFileName(master.Info.InputPath);
            var masters = master.Info.Masters;
            string Id(uint formId) => Identity(formId, masters, fileName);
            foreach (var field in master.Fields)
            {
                if (field is { RecordType: "NPC_", SubrecordType: "FULL" } && field.SourceText.Trim().Length > 0)
                {
                    _npcNames[Id(field.FormId)] = field.SourceText.Trim();
                }
            }

            foreach (var record in master.Records.Values)
            {
                if ((record.Flags & PluginBinary.DeletedFlag) != 0) continue;
                switch (record.Type)
                {
                    case "NPC_":
                        var npc = Id(record.FormId);
                        if (U32(record, "VTCK") is { } voice) _npcVoices[npc] = Id(voice);
                        if (U32(record, "ACBS") is { } flags && (flags & UniqueFlag) != 0) _uniqueNpcs.Add(npc);
                        else _uniqueNpcs.Remove(npc);
                        break;
                    case "ACHR" when U32(record, "NAME") is { } baseNpc:
                        _refs[Id(record.FormId)] = Id(baseNpc);
                        break;
                    case "QUST":
                        ReadAliases(record, Id);
                        break;
                }
            }
        }

        public MasterSpeakerIndex Build()
        {
            // A voice type is one NPC's when every NPC that uses it has the same name (Serana and her cured self).
            var voices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in _npcVoices.GroupBy(pair => pair.Value, pair => pair.Key))
            {
                var npcs = group.ToList();
                var names = npcs.Select(n => _npcNames.GetValueOrDefault(n)).Distinct().ToList();
                if (names is [{ } name] && name.Length > 0)
                {
                    voices[group.Key] = npcs.OrderBy(n => _uniqueNpcs.Contains(n) ? 0 : 1).ThenBy(n => n, StringComparer.Ordinal).First();
                }
            }

            // Only named unique NPCs are kept: a placed guard or bandit is no one speaker.
            var refs = _refs.Where(pair => _uniqueNpcs.Contains(pair.Value) && _npcNames.ContainsKey(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var alias in _aliases.Keys)
            {
                if (Follow(alias, refs, 0) is { } held) aliases[alias] = held;
            }

            return new MasterSpeakerIndex(voices, refs, aliases);
        }

        // What an alias holds: an NPC, the player, a placed actor's NPC, or what another quest's alias holds.
        private string? Follow(string alias, IReadOnlyDictionary<string, string> refs, int depth)
        {
            if (depth > 8 || !_aliases.TryGetValue(alias, out var value)) return null;
            var colon = value.IndexOf(':');
            var kind = colon < 0 ? value : value[..colon];
            var target = colon < 0 ? "" : value[(colon + 1)..];
            return kind switch
            {
                PlayerValue => PlayerValue,
                "npc" => target,
                "ref" => refs.GetValueOrDefault(target),
                "alias" => Follow(target, refs, depth + 1),
                _ => null,
            };
        }

        private void ReadAliases(PluginRecord quest, Func<uint, string> id)
        {
            var questId = id(quest.FormId);
            string? current = null;
            string? externalQuest = null;
            foreach (var sub in quest.Subrecords)
            {
                var data = sub.Data.Span;
                switch (sub.Type)
                {
                    case "ALST" when data.Length == 4:
                        current = questId + ":" + unchecked((int)PluginBinary.U32(data, 0)).ToString(CultureInfo.InvariantCulture);
                        externalQuest = null;
                        _aliases.Remove(current);
                        break;
                    case "ALLS":
                    case "ALED":
                        current = null;
                        break;
                    case "ALUA" when current != null && data.Length == 4:
                        _aliases[current] = "npc:" + id(PluginBinary.U32(data, 0));
                        break;
                    case "ALFR" when current != null && data.Length == 4:
                        var reference = PluginBinary.U32(data, 0);
                        _aliases[current] = reference == SpeakerRef.Player ? PlayerValue : "ref:" + id(reference);
                        break;
                    case "ALEQ" when current != null && data.Length == 4:
                        externalQuest = id(PluginBinary.U32(data, 0));
                        break;
                    case "ALEA" when current != null && externalQuest != null && data.Length == 4:
                        _aliases[current] = "alias:" + externalQuest + ":" + unchecked((int)PluginBinary.U32(data, 0)).ToString(CultureInfo.InvariantCulture);
                        break;
                }
            }
        }

        private static uint? U32(PluginRecord record, string type)
        {
            foreach (var sub in record.Subrecords)
            {
                if (sub.Type == type && sub.Data.Length >= 4) return PluginBinary.U32(sub.Data.Span, 0);
            }

            return null;
        }
    }
}
