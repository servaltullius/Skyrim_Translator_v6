using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Data;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Speech levels decided per speaker and listener, kept in the project (<see cref="SpeakerStyle"/>): the request names
/// a line's decided level next to its speaker, and the quality check compares a line with it instead of with the
/// speaker's other lines. An agent writing a mod decides them as it goes (Alara to the Champion in 해요체, Teldryn in
/// 하오체), and the requests of every lane and the app's later review keep them.
/// </summary>
public static class SpeakerStyles
{
    /// <summary>The listener that is the player.</summary>
    public const string Player = "";

    /// <summary>Another character the plugin does not name (an enemy in combat, an unfilled scene alias).</summary>
    public const string Other = "00000000";

    /// <summary>Anyone the speaker has no level of its own decided for.</summary>
    public const string Anyone = "*";

    /// <summary>The speaker of the player's own lines.</summary>
    public const string PlayerSpeaker = "player";

    public static readonly IReadOnlyList<string> Levels = new[] { "합쇼체", "해요체", "하오체", "하게체", "반말" };

    /// <summary>A level as written (합니다체, 해요, 하오, 반말체 ...) in the list's form; null for anything else.</summary>
    public static string? Normalize(string? level)
    {
        var l = (level ?? "").Trim().Replace(" ", "");
        return l switch
        {
            "합쇼체" or "합쇼" or "합니다체" or "합니다" or "하십시오체" => "합쇼체",
            "해요체" or "해요" => "해요체",
            "하오체" or "하오" => "하오체",
            "하게체" or "하게" => "하게체",
            "반말" or "반말체" or "해체" or "해라체" => "반말",
            _ => null,
        };
    }

    /// <summary>Whether a level is 존댓말 (합쇼체, 해요체); 하오체, 하게체 and 반말 are the other side.</summary>
    public static bool IsPolite(string level) => level is "합쇼체" or "해요체";

    /// <summary>
    /// The level decided for a quality-check group: "SPEAKER" (to the player), "SPEAKER&gt;LISTENER" (a scene's
    /// listener, "00000000" another character, "@jarl" a title the line calls), "player&gt;NPC" (the player's lines to
    /// an NPC); a guessed listener's "?" is not part of it. The pair's own level, else the speaker's level to anyone.
    /// </summary>
    public static string? For(IReadOnlyDictionary<(string Speaker, string Listener), string> styles, string groupKey)
    {
        if (styles.Count == 0)
        {
            return null;
        }

        var key = groupKey.TrimEnd(DialogueSpeakerNames.GuessedListenerMark);
        var separator = key.IndexOf(DialogueSpeakerNames.ListenerSeparator);
        var (speaker, listener) = separator < 0 ? (key, Player) : (key[..separator], key[(separator + 1)..]);
        return styles.TryGetValue((speaker, listener), out var level) ? level
            : styles.TryGetValue((speaker, Anyone), out var any) ? any
            : null;
    }

    public static IReadOnlyDictionary<(string Speaker, string Listener), string> ByPair(IEnumerable<SpeakerStyle> styles)
        => styles.Where(s => Normalize(s.Level) != null)
            .GroupBy(s => (s.Speaker, s.Listener))
            .ToDictionary(g => g.Key, g => Normalize(g.Last().Level)!);
}
