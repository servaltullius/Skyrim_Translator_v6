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
    /// topic's text) it is the NPC the player speaks to, which the request names as "Player, to NAME".
    /// </summary>
    public static async Task<IReadOnlyDictionary<long, string>> LoadAsync(
        IProjectDb db, GameStringDictionary? gameStrings, CancellationToken cancellationToken)
    {
        var speakers = new Dictionary<long, string>(await db.GetStringSpeakersAsync(cancellationToken).ConfigureAwait(false));
        // A topic's text is the player's line; the NPC who answers it is who the player speaks to.
        foreach (var (id, npc) in await db.GetTopicListenersAsync(cancellationToken).ConfigureAwait(false))
        {
            speakers.TryAdd(id, npc);
        }

        if (speakers.Count == 0)
        {
            return speakers;
        }

        var names = await db.GetPluginNpcNamesAsync(cancellationToken).ConfigureAwait(false);
        var plugin = gameStrings == null ? null : await db.GetPluginMastersAsync(cancellationToken).ConfigureAwait(false);
        var labels = new Dictionary<uint, string>();
        var result = new Dictionary<long, string>(speakers.Count);
        foreach (var (id, hex) in speakers)
        {
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var formId))
            {
                continue;
            }

            if (!labels.TryGetValue(formId, out var label))
            {
                var korean = gameStrings != null && plugin is { } p
                    ? gameStrings.FindRecord(GameStringDictionary.FieldIdentity($"NPC_/{formId:X8}/0/FULL/0", p.Masters, p.PluginFileName), "")?.Korean
                    : null;
                label = Label(formId, names.TryGetValue(formId, out var english) ? english : null, korean);
                labels[formId] = label;
            }

            result[id] = label;
        }

        return result;
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
