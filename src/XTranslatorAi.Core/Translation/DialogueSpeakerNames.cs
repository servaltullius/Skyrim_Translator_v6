using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Who says each dialogue line, named for the translation request. Without it the model guessed the speaker from the
/// EditorID and the lines around it, and one NPC's lines came out in two registers: Elenwen's six lines in IDE
/// Thalmor were read as a common Thalmor soldier's and put in 반말. The speaker is the NPC an INFO's conditions, its
/// quest's or its scene name (PluginSpeakers, MasterSpeakerIndex); its name is the plugin's own NPC_ FULL, and for a
/// base game NPC the game translation's Korean name as well. An NPC named by neither still gets a label of its FormID, so its lines are seen as one voice.
/// </summary>
public static class DialogueSpeakerNames
{
    /// <summary>
    /// The speaker's label for each INFO row whose speaker is known, by row id; for a player's line (an INFO's RNAM, a
    /// topic's text) it is the NPC the player speaks to, which the request names as "Player, to NAME"; for a scene line
    /// said to another NPC, both ("Elenwen, to General Tullius").
    /// </summary>
    public static async Task<IReadOnlyDictionary<long, string>> LoadAsync(
        IProjectDb db, GameStringDictionary? gameStrings, CancellationToken cancellationToken)
    {
        var speakers = await LoadFormIdsAsync(db, cancellationToken).ConfigureAwait(false);
        var voices = await LoadVoiceLabelsAsync(db, gameStrings, cancellationToken).ConfigureAwait(false);
        if (speakers.Count == 0)
        {
            return voices;
        }

        var names = await db.GetPluginNpcNamesAsync(cancellationToken).ConfigureAwait(false);
        var sexes = await db.GetPluginNpcSexesAsync(cancellationToken).ConfigureAwait(false);
        var styles = SpeakerStyles.ByPair(await db.GetSpeakerStylesAsync(cancellationToken).ConfigureAwait(false));
        var fieldKeys = styles.Count == 0
            ? new Dictionary<long, string>()
            : await db.GetPluginFieldKeysByStringIdAsync(cancellationToken).ConfigureAwait(false);
        var plugin = await db.GetPluginMastersAsync(cancellationToken).ConfigureAwait(false);
        var labels = new Dictionary<uint, string>();
        var result = new Dictionary<long, string>(speakers.Count);
        foreach (var (id, value) in speakers)
        {
            var separator = value.IndexOf(ListenerSeparator);
            if (!TryFormId(separator < 0 ? value : value[..separator], out var formId))
            {
                continue;
            }

            var label = NpcLabel(formId);
            // A scene line said to another NPC: "Elenwen, to General Tullius", or to one the plugin does not name. A
            // listener the scene only suggests (its other actor) may be the player: "Alara, probably to Marith".
            var heard = separator < 0 ? "" : value[(separator + 1)..];
            var guessed = heard.EndsWith(GuessedListenerMark);
            if (separator >= 0 && TryFormId(guessed ? heard[..^1] : heard, out var listener))
            {
                label += (guessed ? GuessedListenerLabelSeparator : ListenerLabelSeparator)
                         + (listener == PluginSpeakers.UnknownListener ? UnknownListenerLabel : NpcLabel(listener));
            }

            result[id] = label + DecidedStyle(id, value);
        }

        foreach (var (id, label) in voices)
        {
            result.TryAdd(id, label);
        }

        return result;

        // The level decided for the line's speaker and listener (SpeakerStyles); for the player's line (an INFO's RNAM, a
        // topic's text), the player's level to the NPC. A guessed listener's line names both it may be said to.
        string DecidedStyle(long id, string value)
        {
            if (styles.Count == 0)
            {
                return "";
            }

            var key = fieldKeys.GetValueOrDefault(id) ?? "";
            if (key.StartsWith("DIAL/", StringComparison.Ordinal) || key.Contains("/RNAM/", StringComparison.Ordinal))
            {
                return SpeakerStyles.For(styles, SpeakerStyles.PlayerSpeaker + ListenerSeparator + value) is { } player ? $" (정한 말투: {player})" : "";
            }

            if (value.EndsWith(GuessedListenerMark))
            {
                var toListener = SpeakerStyles.For(styles, value);
                var toPlayer = SpeakerStyles.For(styles, value[..value.IndexOf(ListenerSeparator)]);
                return toListener == null && toPlayer == null ? ""
                    : $" (정한 말투: 상대에게 {toListener ?? "정하지 않음"}, 플레이어에게 {toPlayer ?? "정하지 않음"})";
            }

            return SpeakerStyles.For(styles, value) is { } level ? $" (정한 말투: {level})" : "";
        }

        string NpcLabel(uint formId)
        {
            if (!labels.TryGetValue(formId, out var label))
            {
                var korean = gameStrings != null && plugin is { } p
                    ? gameStrings.FindRecord(GameStringDictionary.FieldIdentity($"NPC_/{formId:X8}/0/FULL/0", p.Masters, p.PluginFileName), "")?.Korean
                    : null;
                label = Label(formId, names.TryGetValue(formId, out var english) ? english : null, korean);
                // The plugin's own NPC by its record, a master's by the dictionary: words such as 형/오빠 or 그/그녀 follow it.
                var female = plugin is { } own && formId >> 24 >= own.Masters.Count
                    ? sexes.TryGetValue(formId, out var sex) ? sex : null
                    : plugin is { } m && gameStrings?.Speakers is { } index ? index.IsFemale(formId, m.Masters, m.PluginFileName) : null;
                if (female is { } isFemale) label += isFemale ? FemaleLabel : MaleLabel;
                labels[formId] = label;
            }

            return label;
        }
    }

    /// <summary>After a speaker's name, its sex: "Faryl Atheron (페릴 아세론) (male)".</summary>
    public const string MaleLabel = " (male)";

    public const string FemaleLabel = " (female)";

    /// <summary>Between a scene line's speaker and the NPC it is said to, in the FormID value ("00013269>0001327E").</summary>
    public const char ListenerSeparator = '>';

    /// <summary>Between a scene line's speaker and its listener in the request: "Speaker: Elenwen, to General Tullius".</summary>
    public const string ListenerLabelSeparator = ", to ";

    /// <summary>After a listener the scene only suggests, in the FormID value ("00013269>0001327E?").</summary>
    public const char GuessedListenerMark = '?';

    /// <summary>Between a scene line's speaker and a listener the scene only suggests: "Speaker: Alara, probably to Marith".</summary>
    public const string GuessedListenerLabelSeparator = ", probably to ";

    /// <summary>The listener of a scene line said to a character the plugin does not name (not the player).</summary>
    public const string UnknownListenerLabel = "another character";

    private static bool TryFormId(string hex, out uint formId)
        => uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out formId);

    /// <summary>
    /// The speaker FormID ("%08X") of each INFO row, and for a topic's text (the player's line) the NPC who answers it,
    /// by row id: what the request names and the quality check groups lines by. A scene line said to another NPC adds
    /// that NPC ("00013269>0001327E"), so Elenwen's lines to Tullius are compared apart from her lines to the player.
    /// </summary>
    /// <summary>
    /// "NPC with voice MaleNord" for a line whose speaker only its voice type names, a voice many NPCs share (the
    /// unnamed townsfolk a follower mod has greet him): the voice says a man or a woman, the race, an old or a young
    /// voice. Such lines are in no speaker's group in the quality check; the label is for the request only.
    /// </summary>
    private static async Task<Dictionary<long, string>> LoadVoiceLabelsAsync(IProjectDb db, GameStringDictionary? gameStrings,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<long, string>();
        if (gameStrings?.Speakers is not { } index) return labels;
        var hints = await db.GetStringVoiceHintsAsync(cancellationToken).ConfigureAwait(false);
        if (hints.Count == 0 || await db.GetPluginMastersAsync(cancellationToken).ConfigureAwait(false) is not { } plugin) return labels;
        foreach (var (id, hint) in hints)
        {
            if (index.VoiceName(hint, plugin.Masters, plugin.PluginFileName) is { } voice)
            {
                labels[id] = VoiceLabelPrefix + voice;
            }
        }

        return labels;
    }

    /// <summary>The label of a line its voice type alone names: "NPC with voice MaleNord".</summary>
    public const string VoiceLabelPrefix = "NPC with voice ";

    public static async Task<Dictionary<long, string>> LoadFormIdsAsync(IProjectDb db, CancellationToken cancellationToken)
    {
        var speakers = new Dictionary<long, string>(await db.GetStringSpeakersAsync(cancellationToken).ConfigureAwait(false));
        foreach (var (id, npc) in await db.GetTopicListenersAsync(cancellationToken).ConfigureAwait(false))
        {
            speakers.TryAdd(id, npc);
        }

        return speakers;
    }

    /// <summary>
    /// The quality check's speaker group of each row: <see cref="LoadFormIdsAsync"/>, with an NPC's lines to a title
    /// that is not the player (my Jarl, father) in groups of their own (<see cref="DialogueAddressees"/>). Requests do
    /// not name such a listener: it is a guess from the English, made only to compare lines.
    /// </summary>
    public static async Task<Dictionary<long, string>> LoadCheckGroupsAsync(IProjectDb db, CancellationToken cancellationToken)
    {
        var speakers = await LoadFormIdsAsync(db, cancellationToken).ConfigureAwait(false);
        if (speakers.Count == 0)
        {
            return speakers;
        }

        var rows = await db.GetStringTranslationContextsByIdsAsync(speakers.Keys.ToList(), cancellationToken).ConfigureAwait(false);
        return DialogueAddressees.Apply(speakers,
            rows.Values.Select(row => new DialogueAddressees.Line(row.Id, row.Rec, row.SourceText, row.DialogueScope)));
    }

    internal static string Label(uint formId, string? english, string? korean)
    {
        english = Clean(english);
        korean = Clean(korean);
        if (english != null && korean != null && !string.Equals(english, korean, StringComparison.Ordinal))
        {
            return $"{english} ({korean})";
        }

        return english ?? korean ?? string.Create(CultureInfo.InvariantCulture, $"NPC {formId:X8}");
    }

    private static string? Clean(string? name)
    {
        // A name is one line in the context window and a bracketed prefix on neighbouring lines.
        var trimmed = name?.Replace('\r', ' ').Replace('\n', ' ').Replace('[', '(').Replace(']', ')').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
