using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Who says each dialogue line, named for the translation request. Without it the model guessed the speaker from the
/// EditorID and the lines around it, and one NPC's lines came out in two registers: Elenwen's six lines in IDE
/// Thalmor were read as a common Thalmor soldier's and put in 반말. The speaker is the NPC an INFO's conditions name
/// (PluginReader.FindSpeaker); its name is the plugin's own NPC_ FULL, and for a base game NPC the game translation's
/// Korean name as well. An NPC named by neither still gets a label of its FormID, so its lines are seen as one voice.
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
        if (speakers.Count == 0)
        {
            return speakers;
        }

        var names = await db.GetPluginNpcNamesAsync(cancellationToken).ConfigureAwait(false);
        var plugin = gameStrings == null ? null : await db.GetPluginMastersAsync(cancellationToken).ConfigureAwait(false);
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
            // A scene line said to another NPC: "Elenwen, to General Tullius", or to one the plugin does not name.
            if (separator >= 0 && TryFormId(value[(separator + 1)..], out var listener))
            {
                label += ListenerLabelSeparator + (listener == PluginSceneSpeakers.UnknownListener ? UnknownListenerLabel : NpcLabel(listener));
            }

            result[id] = label;
        }

        return result;

        string NpcLabel(uint formId)
        {
            if (!labels.TryGetValue(formId, out var label))
            {
                var korean = gameStrings != null && plugin is { } p
                    ? gameStrings.FindRecord(GameStringDictionary.FieldIdentity($"NPC_/{formId:X8}/0/FULL/0", p.Masters, p.PluginFileName), "")?.Korean
                    : null;
                label = Label(formId, names.TryGetValue(formId, out var english) ? english : null, korean);
                labels[formId] = label;
            }

            return label;
        }
    }

    /// <summary>Between a scene line's speaker and the NPC it is said to, in the FormID value ("00013269>0001327E").</summary>
    public const char ListenerSeparator = '>';

    /// <summary>Between a scene line's speaker and its listener in the request: "Speaker: Elenwen, to General Tullius".</summary>
    public const string ListenerLabelSeparator = ", to ";

    /// <summary>The listener of a scene line said to a character the plugin does not name (not the player).</summary>
    public const string UnknownListenerLabel = "another character";

    private static bool TryFormId(string hex, out uint formId)
        => uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out formId);

    /// <summary>
    /// The speaker FormID ("%08X") of each INFO row, and for a topic's text (the player's line) the NPC who answers it,
    /// by row id: what the request names and the quality check groups lines by. A scene line said to another NPC adds
    /// that NPC ("00013269>0001327E"), so Elenwen's lines to Tullius are compared apart from her lines to the player.
    /// </summary>
    public static async Task<Dictionary<long, string>> LoadFormIdsAsync(IProjectDb db, CancellationToken cancellationToken)
    {
        var speakers = new Dictionary<long, string>(await db.GetStringSpeakersAsync(cancellationToken).ConfigureAwait(false));
        foreach (var (id, npc) in await db.GetTopicListenersAsync(cancellationToken).ConfigureAwait(false))
        {
            speakers.TryAdd(id, npc);
        }

        return speakers;
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
