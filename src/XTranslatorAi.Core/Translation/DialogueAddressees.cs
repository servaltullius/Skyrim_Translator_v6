using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Whom an NPC's line outside a scene is said to, when it is not the player, for the quality check's speaker groups.
/// Serana's lines to Jarl Elisif and to her father were compared with her lines to the player and warned as the odd
/// 존댓말 ones. Only a title that never names the player counts (my Jarl, father, Your Majesty), and only where it
/// calls someone: "There you are, father!" is said to Harkon, "My father would never..." is not. A name proves
/// nothing, since Serana Dialogue Add-On has Serana call the player by name ("Hey Joshua!").
/// <para>
/// Within one topic, the speaker's lines that call no one are said to the same listener when the others call that one
/// title and nobody else: Elisif's banter has "My Jarl." beside "So, what do you think of my outfit?".
/// </para>
/// </summary>
public static class DialogueAddressees
{
    /// <summary>Before an addressee in a quality-check group: "02002B6C>@jarl".</summary>
    public const string AddresseePrefix = "@";

    public readonly record struct Line(long Id, string? Rec, string? Source, string? Topic);

    private const string Title = @"(?<title>my\s+jarl|jarl(?:\s+(?-i:[A-Z][a-z]+))?|my\s+(?:king|queen)|father|mother|your\s+(?:majesty|highness|grace))";

    // "My Jarl, how should I put this...", "Hey, um - My Jarl.", "Father!"
    private static readonly Regex LeadingTitle = new(
        @"^(?:\*[^*]*\*\s*)?(?:(?:hey|um+|uh+|oh|ah|well|so|now|excuse\s+me|pardon\s+me)\b[\s,.\-–—…]*)*" + Title + @"\s*(?:[,!?.…\-–—]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "There you are, father!", "So, what do you think of Nazeem - my Jarl?", "May I... my Jarl?"
    private static readonly Regex TrailingTitle = new(
        @"(?:,|\.\.\.|…|\s[\-–—])\s*" + Title + @"\s*[.!?…]*\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string Name = @"(?<name>[A-Z][a-z]+(?:[-'][A-Za-z]+)?)";

    // "Hey Joshua!", "Sybille, how long...", "Damnit Horizon, did you...", "Oh really, Belethor?": someone is called,
    // and it may be the player.
    private static readonly Regex LeadingName = new(
        @"^(?:\*[^*]*\*\s*)?(?:(?:Hey|Oh|Ah|Well|So|Now|Listen|Look|Okay|Hello|Hi|Damn|Damnit|Dammit|Gods|Sorry|Thanks|Goodnight|Welcome|Wait|Come\s+on)[,!]?\s+)?"
        + Name + @"\s*[,!?]",
        RegexOptions.CultureInvariant);

    private static readonly Regex TrailingName = new(@",\s*" + Name + @"\s*[.!?…]*\s*$", RegexOptions.CultureInvariant);

    // Capitalized words that begin or end a sentence without calling anyone.
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "no", "well", "oh", "ah", "hey", "okay", "ok", "alright", "fine", "sure", "look", "listen", "now", "so",
        "then", "still", "indeed", "hmm", "huh", "ugh", "wait", "please", "thanks", "sorry", "gods", "damn", "damnit",
        "honestly", "actually", "anyway", "besides", "really", "right", "good", "great", "perfect", "wow", "hello", "hi",
        "greetings", "farewell", "goodbye", "careful", "quiet", "enough", "stop", "easy", "come", "go", "run", "help",
        "true", "but", "and", "or", "yeah", "yep", "nope", "um", "uh", "er", "eh", "see", "hah", "ha", "heh", "aww",
        "what", "why", "how", "who", "where", "when", "maybe", "perhaps", "never", "always", "again", "here", "there",
        "though", "however", "finally", "seriously", "sometimes", "supposedly", "remember", "regardless", "relax",
        "whew", "whoa", "haha", "hmph", "anytime", "goodnight", "finished", "agreed", "understood", "certainly",
        "absolutely", "exactly", "excuse", "pardon", "thank", "ready", "done", "deal", "nice", "cool", "alas", "yikes",
    };

    /// <summary>The title a line calls someone other than the player by ("jarl", "father"), or null.</summary>
    public static string? Find(string? line)
    {
        var text = (line ?? "").Trim();
        var match = LeadingTitle.Match(text);
        if (!match.Success) match = TrailingTitle.Match(text);
        if (!match.Success) return null;

        var title = match.Groups["title"].Value.ToLowerInvariant();
        return title.Contains("jarl", StringComparison.Ordinal) ? "jarl"
            : title.Contains("king", StringComparison.Ordinal) ? "king"
            : title.Contains("queen", StringComparison.Ordinal) ? "queen"
            : title.StartsWith("your", StringComparison.Ordinal) ? "majesty"
            : title;
    }

    /// <summary>Whether the line calls anyone, by a title or by a name (the player's too).</summary>
    public static bool CallsSomeone(string? line)
    {
        var text = (line ?? "").Trim();
        return Find(text) != null || IsName(LeadingName.Match(text)) || IsName(TrailingName.Match(text));
    }

    private static bool IsName(Match match) => match.Success && !NotNames.Contains(match.Groups["name"].Value);

    /// <summary>
    /// The quality-check groups with each NPC line said to a title moved to its own group ("speaker>@jarl"): the
    /// line that calls the title, and the speaker's other lines of the topic when the topic calls no one else.
    /// Groups that already name a listener (a scene) and the player's lines are left as they are.
    /// </summary>
    public static Dictionary<long, string> Apply(IReadOnlyDictionary<long, string> keys, IEnumerable<Line> lines)
    {
        var result = new Dictionary<long, string>(keys);
        // A line to the player, or to a listener the scene only suggests (Speaker>Listener?): a title the line calls
        // says more than the guess. Serana's "There you are, father!" is in a scene whose other actor is not Harkon, and
        // compared with her lines to that actor and to the player, both 반말, her 존댓말 to her father was flagged.
        static string? SpeakerOf(string key)
        {
            var separator = key.IndexOf(DialogueSpeakerNames.ListenerSeparator);
            return separator < 0 ? key : key.EndsWith(DialogueSpeakerNames.GuessedListenerMark) ? key[..separator] : null;
        }

        var npcLines = lines
            .Where(line => TranslationRowRules.IsNpcLine(line.Rec)
                           && keys.TryGetValue(line.Id, out var key) && SpeakerOf(key) != null)
            .ToList();
        foreach (var group in npcLines.GroupBy(line => (line.Topic, Speaker: SpeakerOf(keys[line.Id])!)))
        {
            var titles = group.Select(line => (line, title: Find(line.Source))).ToList();
            var called = titles.Where(t => t.title != null).Select(t => t.title!).Distinct(StringComparer.Ordinal).ToList();
            if (called.Count == 0) continue;

            // One title, called by at least half of the speaker's lines of the topic, and no one else called: the lines
            // that call no one are said to the same listener. Serana's "There you are, father!" among 19 battle cries
            // says nothing about the others.
            var shared = group.Key.Topic != null && called.Count == 1
                         && titles.Count(t => t.title != null) * 2 >= titles.Count
                         && titles.All(t => t.title != null || !CallsSomeone(t.line.Source))
                ? called[0]
                : null;
            foreach (var (line, title) in titles)
            {
                if ((title ?? shared) is { } addressee)
                {
                    result[line.Id] = group.Key.Speaker + DialogueSpeakerNames.ListenerSeparator + AddresseePrefix + addressee;
                }
            }
        }

        return result;
    }
}
