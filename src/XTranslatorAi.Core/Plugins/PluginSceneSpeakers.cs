using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// Who says a scene's lines, and to whom. A scene's INFOs carry no GetIsID condition: the scene's dialogue action names
/// the alias that speaks (ALID) and the alias it looks at (HTID), and the scene's quest fills each alias with an NPC
/// (ALUA) or a placed actor (ALFR). Without this all 73 scene lines of IDE Thalmor went to the model with no speaker,
/// and Elenwen's lines to General Tullius came out in 반말 from both the app and an agent run.
/// <para>
/// Only what the plugin itself says is used: an alias filled by a master's placed actor, or a quest the plugin does not
/// carry, gives no speaker. A topic said by two different speakers (two scenes, or two actions) gives none either. Such
/// an alias as the listener is UnknownListener: the line is said to someone other than the player.
/// </para>
/// </summary>
internal static class PluginSceneSpeakers
{
    internal readonly record struct SceneSpeaker(uint Speaker, uint? Listener);

    /// <summary>The listener of a line said to a character the plugin does not name: someone other than the player.</summary>
    public const uint UnknownListener = 0;

    private const uint PlayerRef = 0x14;
    private const ushort DialogueAction = 0;

    internal static IReadOnlyDictionary<uint, SceneSpeaker> Build(IEnumerable<PluginRecord> records)
    {
        var scenes = new List<PluginRecord>();
        var quests = new Dictionary<uint, PluginRecord>();
        var placedActors = new Dictionary<uint, uint>();
        foreach (var record in records)
        {
            if ((record.Flags & PluginBinary.DeletedFlag) != 0) continue;
            switch (record.Type)
            {
                case "SCEN": scenes.Add(record); break;
                case "QUST": quests[record.FormId] = record; break;
                case "ACHR" when U32(record, "NAME") is { } baseNpc: placedActors[record.FormId] = baseNpc; break;
            }
        }

        var result = new Dictionary<uint, SceneSpeaker>();
        if (scenes.Count == 0) return result;
        var conflicted = new HashSet<uint>();
        foreach (var scene in scenes)
        {
            var (actors, actions, questId) = ReadScene(scene);
            if (questId is not { } id || !quests.TryGetValue(id, out var quest)) continue;
            var aliases = ReadAliases(quest, placedActors);
            foreach (var (alias, topic, target) in actions)
            {
                if (conflicted.Contains(topic)) continue;
                if (!aliases.TryGetValue(alias, out var speaker) || speaker is not { } npc || npc == PlayerRef)
                {
                    // An unknown voice says this topic too, so no one speaker can be named for it.
                    result.Remove(topic);
                    conflicted.Add(topic);
                    continue;
                }

                // The alias the speaker looks at; without one, the other actor of a two-actor scene.
                var listenerAlias = target is { } t && t >= 0 && t != alias ? t
                    : actors.Count == 2 && actors.Contains(alias) ? Other(actors, alias) : (int?)null;
                // An alias the plugin leaves unfilled (Actor1, a master's placed actor) is still someone in the scene, not
                // the player: Elenwen's curt lines to her agents were compared with her polite lines to the player.
                uint? listener = listenerAlias is not { } la || !aliases.TryGetValue(la, out var heard) ? null
                    : heard is not { } h ? UnknownListener
                    : h == PlayerRef || h == npc ? null
                    : h;
                var entry = new SceneSpeaker(npc, listener);
                if (!result.TryGetValue(topic, out var seen))
                {
                    result[topic] = entry;
                }
                else if (seen.Speaker != npc)
                {
                    result.Remove(topic);
                    conflicted.Add(topic);
                }
                else if (seen.Listener != listener)
                {
                    result[topic] = seen with { Listener = null };
                }
            }
        }

        return result;
    }

    private static int Other(List<int> actors, int alias) => actors[0] == alias ? actors[1] : actors[0];

    /// <summary>
    /// The scene's actor aliases, its dialogue actions (speaking alias, topic, head-track alias) and its quest (the PNAM
    /// after the actions; a package action's PNAM is its package).
    /// </summary>
    private static (List<int> Actors, List<(int Alias, uint Topic, int? Target)> Actions, uint? Quest) ReadScene(PluginRecord scene)
    {
        var actors = new List<int>();
        var actions = new List<(int, uint, int?)>();
        uint? quest = null;
        var inAction = false;
        ushort type = 0;
        var alias = -1;
        uint topic = 0;
        int? target = null;
        foreach (var sub in scene.Subrecords)
        {
            var data = sub.Data.Span;
            switch (sub.Type)
            {
                case "ANAM" when data.Length == 2:
                    inAction = true;
                    type = PluginBinary.U16(data, 0);
                    alias = -1;
                    topic = 0;
                    target = null;
                    break;
                case "ANAM":
                    if (inAction && type == DialogueAction && alias >= 0 && topic != 0) actions.Add((alias, topic, target));
                    inAction = false;
                    break;
                case "ALID" when data.Length == 4:
                    var value = unchecked((int)PluginBinary.U32(data, 0));
                    if (inAction) alias = value;
                    else if (!actors.Contains(value)) actors.Add(value);
                    break;
                case "DATA" when inAction && data.Length == 4:
                    topic = PluginBinary.U32(data, 0);
                    break;
                case "HTID" when inAction && data.Length == 4:
                    target = unchecked((int)PluginBinary.U32(data, 0));
                    break;
                case "PNAM" when !inAction && data.Length == 4:
                    quest = PluginBinary.U32(data, 0);
                    break;
            }
        }

        return (actors, actions, quest);
    }

    /// <summary>Each reference alias of the quest and the NPC that fills it (PlayerRef for the player), when the plugin says.</summary>
    private static Dictionary<int, uint?> ReadAliases(PluginRecord quest, IReadOnlyDictionary<uint, uint> placedActors)
    {
        var aliases = new Dictionary<int, uint?>();
        int? current = null;
        foreach (var sub in quest.Subrecords)
        {
            var data = sub.Data.Span;
            switch (sub.Type)
            {
                case "ALST" when data.Length == 4:
                    current = unchecked((int)PluginBinary.U32(data, 0));
                    aliases[current.Value] = null;
                    break;
                case "ALLS":
                case "ALED":
                    current = null;
                    break;
                case "ALUA" when current is { } a && data.Length == 4:
                    aliases[a] = PluginBinary.U32(data, 0);
                    break;
                case "ALFR" when current is { } a && data.Length == 4:
                    var reference = PluginBinary.U32(data, 0);
                    aliases[a] = reference == PlayerRef ? PlayerRef
                        : placedActors.TryGetValue(reference, out var baseNpc) ? baseNpc : null;
                    break;
            }
        }

        return aliases;
    }

    private static uint? U32(PluginRecord record, string type)
    {
        foreach (var sub in record.Subrecords)
        {
            if (sub.Type == type && sub.Data.Length == 4) return PluginBinary.U32(sub.Data.Span, 0);
        }

        return null;
    }
}
