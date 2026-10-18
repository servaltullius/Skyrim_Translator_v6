using System;
using System.Collections.Generic;
using System.Globalization;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// A dialogue line's speaker or listener: an NPC the plugin names, or a reference only a master can resolve (a voice
/// type, a master's placed actor or quest alias), as a hint for <see cref="MasterSpeakerIndex"/>.
/// </summary>
public readonly record struct SpeakerRef(uint? Npc, string? Hint)
{
    /// <summary>The player's reference (PlayerRef), as an alias names it.</summary>
    public const uint Player = 0x14;

    public bool IsPlayer => Npc == Player;

    public static SpeakerRef Of(uint npc) => new(npc, null);

    /// <summary>A voice type's one NPC: "V:02002B6F".</summary>
    public static SpeakerRef VoiceType(uint voiceType) => new(null, string.Create(CultureInfo.InvariantCulture, $"V:{voiceType:X8}"));

    /// <summary>A placed actor's base NPC: "R:0200A1B2".</summary>
    public static SpeakerRef PlacedActor(uint reference) => new(null, string.Create(CultureInfo.InvariantCulture, $"R:{reference:X8}"));

    /// <summary>The NPC a quest alias holds: "A:0200C3D4:12".</summary>
    public static SpeakerRef QuestAlias(uint quest, int alias) => new(null, string.Create(CultureInfo.InvariantCulture, $"A:{quest:X8}:{alias}"));
}

/// <summary>
/// Who says each dialogue line of a plugin, and for a scene line to whom, from what the plugin itself records.
/// <list type="bullet">
/// <item>The line's conditions: GetIsID names the NPC; GetIsAliasRef names an alias of the topic's quest, which the
/// quest fills with an NPC (ALUA), a placed actor (ALFR) or another quest's alias (ALEQ/ALEA); GetIsVoiceType names a
/// voice type, which may be one NPC's own.</item>
/// <item>A scene's dialogue action: the alias that speaks (ALID) and the one it looks at (HTID). Without it all 73 scene
/// lines of IDE Thalmor had no speaker, and Elenwen's lines to General Tullius came out in 반말.</item>
/// <item>The quest's dialogue conditions, which every line of the quest must meet: Serana Dialogue Add-On names Serana
/// once for a whole quest of her lines (GetIsID, or her voice type).</item>
/// </list>
/// What only a master knows (its placed actors, its quests, the NPCs that use a voice type) is left as a hint.
/// </summary>
internal sealed class PluginSpeakers
{
    /// <summary>The listener of a line said to a character the plugin does not name: someone other than the player.</summary>
    public const uint UnknownListener = 0;

    private const ushort GetIsId = 72;
    private const ushort GetIsVoiceType = 426;
    private const ushort GetIsAliasRef = 566;
    private const ushort DialogueAction = 0;

    private enum ConditionKind { Npc, Alias, VoiceType }

    private readonly record struct SpeakerCondition(ConditionKind Kind, uint Value);

    private readonly int _masterCount;
    private readonly Dictionary<uint, PluginRecord> _quests = new();
    private readonly Dictionary<uint, uint> _topicQuests = new();
    private readonly Dictionary<uint, uint> _placedActors = new();
    private readonly Dictionary<uint, uint?> _voiceNpcs = new();
    private readonly Dictionary<uint, Dictionary<int, SpeakerRef?>> _aliases = new();
    private readonly Dictionary<uint, SpeakerRef?> _questSpeakers = new();
    private readonly Dictionary<uint, (SpeakerRef Speaker, SpeakerRef? Listener)> _scenes = new();

    private PluginSpeakers(int masterCount) => _masterCount = masterCount;

    internal static PluginSpeakers Build(IEnumerable<PluginRecord> records, int masterCount)
    {
        var speakers = new PluginSpeakers(masterCount);
        var scenes = new List<PluginRecord>();
        foreach (var record in records)
        {
            if ((record.Flags & PluginBinary.DeletedFlag) != 0) continue;
            switch (record.Type)
            {
                case "SCEN": scenes.Add(record); break;
                case "QUST": speakers._quests[record.FormId] = record; break;
                case "DIAL" when U32(record, "QNAM") is { } quest: speakers._topicQuests[record.FormId] = quest; break;
                case "ACHR" when U32(record, "NAME") is { } baseNpc: speakers._placedActors[record.FormId] = baseNpc; break;
                case "NPC_" when U32(record, "VTCK") is { } voice:
                    // A voice type two of the plugin's NPCs use names neither.
                    speakers._voiceNpcs[voice] = speakers._voiceNpcs.TryGetValue(voice, out var other) && other != record.FormId ? null : record.FormId;
                    break;
            }
        }

        speakers.ReadScenes(scenes);
        return speakers;
    }

    /// <summary>An INFO's speaker, and for a scene line the character it is said to (null: the player, or no one known).</summary>
    internal (SpeakerRef? Speaker, SpeakerRef? Listener) ForInfo(PluginRecord info)
    {
        var topic = info.DialogueTopicFormId;
        uint? quest = topic is { } t && _topicQuests.TryGetValue(t, out var q) ? q : null;
        var own = Resolve(FindCondition(info.Subrecords), quest);
        if (topic is { } sceneTopic && _scenes.TryGetValue(sceneTopic, out var scene))
        {
            // A scene's listener counts only when the scene's speaker is the line's.
            if (own == null || own == scene.Speaker) return (scene.Speaker, scene.Listener);
            return (own, null);
        }

        return (own ?? (quest is { } owner ? QuestSpeaker(owner) : null), null);
    }

    private SpeakerRef? QuestSpeaker(uint quest)
    {
        if (_questSpeakers.TryGetValue(quest, out var known)) return known;
        SpeakerRef? speaker = null;
        if (_quests.TryGetValue(quest, out var record))
        {
            // The dialogue conditions come first, before the event conditions (after NEXT) and the stages.
            var dialogueConditions = new List<PluginSubrecord>();
            foreach (var sub in record.Subrecords)
            {
                if (sub.Type is "NEXT" or "INDX" or "QOBJ" or "ALST" or "ALLS" or "ANAM") break;
                if (sub.Type == "CTDA") dialogueConditions.Add(sub);
            }

            speaker = Resolve(FindCondition(dialogueConditions), quest);
        }

        _questSpeakers[quest] = speaker;
        return speaker;
    }

    private SpeakerRef? Resolve(SpeakerCondition? condition, uint? quest)
    {
        var speaker = condition switch
        {
            { Kind: ConditionKind.Npc } c => SpeakerRef.Of(c.Value),
            { Kind: ConditionKind.Alias } c when quest is { } q => ResolveAlias(q, unchecked((int)c.Value), 0),
            { Kind: ConditionKind.VoiceType } c => ResolveVoice(c.Value),
            _ => (SpeakerRef?)null,
        };
        return speaker is { IsPlayer: true } ? null : speaker;
    }

    // A voice type of the plugin's own is resolved here; a master's may be shared by NPCs only the masters know.
    private SpeakerRef? ResolveVoice(uint voice)
        => voice >> 24 < _masterCount ? SpeakerRef.VoiceType(voice)
            : _voiceNpcs.TryGetValue(voice, out var npc) && npc is { } n ? SpeakerRef.Of(n) : null;

    /// <summary>What a quest alias holds; null when the alias is empty or filled at run time (Find Matching Reference).</summary>
    private SpeakerRef? ResolveAlias(uint quest, int alias, int depth)
    {
        if (depth > 8) return null;
        if (!_quests.TryGetValue(quest, out var record)) return SpeakerRef.QuestAlias(quest, alias);
        if (!_aliases.TryGetValue(quest, out var aliases))
        {
            aliases = ReadAliases(record, depth);
            _aliases[quest] = aliases;
        }

        return aliases.TryGetValue(alias, out var held) ? held : null;
    }

    private Dictionary<int, SpeakerRef?> ReadAliases(PluginRecord quest, int depth)
    {
        var aliases = new Dictionary<int, SpeakerRef?>();
        int? current = null;
        uint? externalQuest = null;
        foreach (var sub in quest.Subrecords)
        {
            var data = sub.Data.Span;
            switch (sub.Type)
            {
                case "ALST" when data.Length == 4:
                    current = unchecked((int)PluginBinary.U32(data, 0));
                    externalQuest = null;
                    aliases[current.Value] = null;
                    break;
                case "ALLS":
                case "ALED":
                    current = null;
                    break;
                case "ALUA" when current is { } a && data.Length == 4:
                    aliases[a] = SpeakerRef.Of(PluginBinary.U32(data, 0));
                    break;
                case "ALFR" when current is { } a && data.Length == 4:
                    var reference = PluginBinary.U32(data, 0);
                    aliases[a] = reference == SpeakerRef.Player ? SpeakerRef.Of(SpeakerRef.Player)
                        : _placedActors.TryGetValue(reference, out var baseNpc) ? SpeakerRef.Of(baseNpc)
                        : reference >> 24 < _masterCount ? SpeakerRef.PlacedActor(reference)
                        : null;
                    break;
                // An alias that takes another quest's alias (Serana's in Dawnguard's quests) holds what that one holds.
                case "ALEQ" when current != null && data.Length == 4:
                    externalQuest = PluginBinary.U32(data, 0);
                    break;
                case "ALEA" when current is { } a && externalQuest is { } other && data.Length == 4:
                    aliases[a] = ResolveAlias(other, unchecked((int)PluginBinary.U32(data, 0)), depth + 1);
                    break;
            }
        }

        return aliases;
    }

    private void ReadScenes(List<PluginRecord> scenes)
    {
        var conflicted = new HashSet<uint>();
        foreach (var scene in scenes)
        {
            var (actors, actions, quest) = ReadScene(scene);
            if (quest is not { } questId) continue;
            foreach (var (alias, topic, target) in actions)
            {
                if (conflicted.Contains(topic)) continue;
                if (ResolveAlias(questId, alias, 0) is not { IsPlayer: false } speaker)
                {
                    // An unknown voice says this topic too, so no one speaker can be named for it.
                    _scenes.Remove(topic);
                    conflicted.Add(topic);
                    continue;
                }

                // The alias the speaker looks at; without one, the other actor of a two-actor scene. An alias the
                // plugin leaves unfilled is still someone in the scene, not the player: Elenwen's curt lines to her
                // agents were compared with her polite lines to the player.
                var listenerAlias = target is { } t && t >= 0 && t != alias ? t
                    : actors.Count == 2 && actors.Contains(alias) ? (actors[0] == alias ? actors[1] : actors[0]) : (int?)null;
                SpeakerRef? listener = listenerAlias is not { } la ? null
                    : ResolveAlias(questId, la, 0) is not { } heard ? SpeakerRef.Of(UnknownListener)
                    : heard.IsPlayer || heard == speaker ? null
                    : heard;
                if (!_scenes.TryGetValue(topic, out var seen))
                {
                    _scenes[topic] = (speaker, listener);
                }
                else if (seen.Speaker != speaker)
                {
                    _scenes.Remove(topic);
                    conflicted.Add(topic);
                }
                else if (seen.Listener != listener)
                {
                    _scenes[topic] = (speaker, null);
                }
            }
        }
    }

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

    /// <summary>
    /// The speaker condition of a condition list, run on the subject and == 1: GetIsID over GetIsAliasRef over
    /// GetIsVoiceType. None when such a condition is ORed with another (any Thalmor may say "GetIsID Elenwen OR
    /// GetInFaction ThalmorFaction"), or when two of a kind disagree.
    /// </summary>
    private static SpeakerCondition? FindCondition(IEnumerable<PluginSubrecord> subrecords)
    {
        var found = new Dictionary<ConditionKind, uint?>();
        var previousOr = false;
        foreach (var sub in subrecords)
        {
            if (sub.Type != "CTDA" || sub.Data.Length < 24) continue;
            var data = sub.Data.Span;
            var or = (data[0] & 0x01) != 0;
            var joined = or || previousOr;
            previousOr = or;
            var op = data[0] >> 5;
            var usesGlobal = (data[0] & 0x04) != 0;
            var value = BitConverter.Int32BitsToSingle(unchecked((int)PluginBinary.U32(data, 4)));
            var function = (ushort)(data[8] | (data[9] << 8));
            var runOn = PluginBinary.U32(data, 20);
            ConditionKind? kind = function switch
            {
                GetIsId => ConditionKind.Npc,
                GetIsAliasRef => ConditionKind.Alias,
                GetIsVoiceType => ConditionKind.VoiceType,
                _ => null,
            };
            if (kind is not { } k || op != 0 || usesGlobal || value != 1f || runOn != 0) continue;
            if (joined) return null;
            var parameter = PluginBinary.U32(data, 12);
            found[k] = found.TryGetValue(k, out var seen) && seen != parameter ? null : parameter;
        }

        foreach (var kind in new[] { ConditionKind.Npc, ConditionKind.Alias, ConditionKind.VoiceType })
        {
            if (found.TryGetValue(kind, out var parameter)) return parameter is { } p ? new SpeakerCondition(kind, p) : null;
        }

        return null;
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
