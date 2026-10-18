using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// One NPC's lines across every topic of the plugin: when nearly all of them are 존댓말 (or 반말), a line in the other
/// level is flagged. The dialogue-group check compares lines of one topic only, so in IDE Thalmor Ondolemar went from
/// 하오 to 해라 to 합니다 to 해요 from one topic to the next with three warnings in all. 합니다체 and 해요체 mix
/// naturally in polite speech, so the levels compared are only 존댓말 and 반말.
/// <para>
/// A speaker with too few lines for a clear majority, or with the lines split (4 to 3), was not shown at all: in Fists
/// of Fury, Reggr's one 존댓말 line among his 반말 ones passed. Such a split is listed for reference (Info), as the
/// player's lines are: the player's level follows who is spoken to, so the player's lines to one NPC (an INFO's RNAM
/// or a topic's text, whose Speaker is that NPC) keep one level, but lines to different NPCs may differ.
/// </para>
/// </summary>
internal static class SpeakerToneConsistencyRule
{
    public const string Code = "speaker_tone_inconsistent";
    public const string MixedCode = "speaker_tone_mixed";
    public const string PlayerCode = "player_tone_inconsistent";

    private const string PlayerKeyPrefix = "player>";

    // A scene line said to a character the plugin does not name (PluginSpeakers.UnknownListener, as "%08X").
    private const string UnknownListener = "00000000";

    private static readonly System.Text.RegularExpressions.Regex SpeechCheck = new(
        @"\((?:Persuade|Intimidate|Bribe|Lie|Brawl|Threaten)[^)]*\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public sealed record Levels(IReadOnlyDictionary<string, ToneKind> Majorities, IReadOnlyDictionary<string, (int Polite, int Casual)> Counts);

    public static Levels BuildSpeakerMajorities(IReadOnlyList<LqaScanEntry> entries)
    {
        var levels = new Dictionary<string, List<ToneKind>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (Key(entry) is not { } key)
            {
                continue;
            }

            if (!levels.TryGetValue(key, out var list))
            {
                list = new List<ToneKind>();
                levels[key] = list;
            }

            list.Add(Level(entry.DestText));
        }

        var majorities = new Dictionary<string, ToneKind>(StringComparer.Ordinal);
        var counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var (key, list) in levels)
        {
            if (!key.StartsWith(PlayerKeyPrefix, StringComparison.Ordinal) && LqaToneClassifier.TryGetStrongMajorityTone(list, out var majority))
            {
                majorities[key] = majority;
            }

            counts[key] = (list.Count(l => l == ToneKind.Hamnida), list.Count(l => l == ToneKind.Casual));
        }

        return new Levels(majorities, counts);
    }

    public static void Apply(LqaScanEntry entry, Levels levels, List<LqaIssue> issues)
    {
        if (Key(entry) is not { } key)
        {
            return;
        }

        var level = Level(entry.DestText);
        if (level == ToneKind.Unknown)
        {
            return;
        }

        var player = key.StartsWith(PlayerKeyPrefix, StringComparison.Ordinal);
        var who = key.EndsWith(DialogueSpeakerNames.ListenerSeparator + UnknownListener, StringComparison.Ordinal) ? "같은 화자가 장면에서 다른 인물에게 한"
            : key.Contains(DialogueSpeakerNames.ListenerSeparator) ? "같은 화자가 같은 상대에게 한"
            : "같은 화자의";
        if (!player && levels.Majorities.TryGetValue(key, out var majority))
        {
            if (level != majority)
            {
                issues.Add(Issue(entry, "Warn", Code, $"{who} 다른 대사와 말투가 다릅니다: 대부분 {Display(majority)}, 이 대사 {Display(level)}"));
            }

            return;
        }

        var (polite, casual) = levels.Counts[key];
        if (polite == 0 || casual == 0)
        {
            return;
        }

        // The smaller side is the odd one out; with as many of each, every line is listed.
        var mine = level == ToneKind.Hamnida ? polite : casual;
        var other = level == ToneKind.Hamnida ? casual : polite;
        if (mine > other)
        {
            return;
        }

        var counted = $"존댓말 {polite}줄, 반말 {casual}줄, 이 줄 {Display(level)}";
        issues.Add(player
            ? Issue(entry, "Info", PlayerCode, $"같은 상대에게 하는 플레이어 선택지끼리 말투가 다릅니다({counted})")
            : Issue(entry, "Info", MixedCode, $"{who} 대사끼리 말투가 갈립니다({counted})"));
    }

    private static LqaIssue Issue(LqaScanEntry entry, string severity, string code, string message)
        => new(
            Id: entry.Id,
            OrderIndex: entry.OrderIndex,
            Edid: entry.Edid,
            Rec: entry.Rec,
            Severity: severity,
            Code: code,
            Message: message,
            SourceText: entry.SourceText ?? "",
            DestText: entry.DestText ?? "");

    /// <summary>
    /// The group a line is compared in: an NPC's lines (INFO:NAM1) by speaker, and a scene line said to another NPC by
    /// the pair ("speaker>listener": Elenwen speaks up to General Tullius and down to her agents; a listener the plugin
    /// does not name is "00000000", lines to someone other than the player); the player's lines
    /// (INFO:RNAM, DIAL:FULL) by the NPC they are said to. Null for a line with no known speaker or not translated yet.
    /// </summary>
    private static string? Key(LqaScanEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Speaker) || entry.Status is not (StringEntryStatus.Done or StringEntryStatus.Edited))
        {
            return null;
        }

        var rec = entry.Rec?.Trim().ToUpperInvariant();
        return rec switch
        {
            "INFO:NAM1" => entry.Speaker,
            // A speech-check choice ((Intimidate), (Brawl) ...) takes its own attitude, apart from the player's others.
            "INFO:RNAM" or "DIAL:FULL" when !SpeechCheck.IsMatch(entry.SourceText ?? "") => PlayerKeyPrefix + entry.Speaker,
            _ => null,
        };
    }

    /// <summary>
    /// 존댓말 when any sentence or clause of the line is: "어디 한번 보죠. 당신이 어떤 실력인지." and "물론이죠, 돈만
    /// 있다면야." are polite lines that trail off. Otherwise the last sentence's level.
    /// </summary>
    private static ToneKind Level(string? text)
    {
        var unquoted = Quoted.Replace(text ?? "", " ").Trim();
        if (unquoted.Any(char.IsLetter))
        {
            text = unquoted;
        }

        var parts = (text ?? "").Split(new[] { '.', '!', '?', '…', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1 && parts.Any(part => SentenceLevel(part) == ToneKind.Hamnida))
        {
            return ToneKind.Hamnida;
        }

        return SentenceLevel(text);
    }

    /// <summary>존댓말 (Hamnida) or 반말 (Casual) by the last sentence's ending; a lone word in 다 is usually a name.</summary>
    private static ToneKind SentenceLevel(string? text)
    {
        var tone = LqaToneClassifier.Classify(text);
        var level = tone switch
        {
            ToneKind.Hamnida or ToneKind.Haeyo => ToneKind.Hamnida,
            ToneKind.Casual => ToneKind.Casual,
            ToneKind.PlainDa => LqaScanner.StripUiTokens(text ?? "").Trim().Any(char.IsWhiteSpace) ? ToneKind.Casual : ToneKind.Unknown,
            _ => ToneKind.Unknown,
        };
        return level == ToneKind.Unknown ? SpokenLevel(text) : level;
    }

    // The shared classifier knows 반말 only by 해/야/지/냐, so "훌륭했어!", "다시 와.", "덤벼 봐." and "물론이죠." had no
    // level and a speaker's odd line passed. Dialogue lines only (item text is classified by the shared rules). Not 네
    // (the polite "yes"), 게, 데 or 래, which end nouns as often as 반말 ("여긴 내 가게.", "그런데...", "노래.").
    private static readonly string[] CasualEndings =
    {
        "어", "아", "봐", "줘", "와", "워", "돼", "걸", "구나", "렴",
        "는군", "었군", "았군", "겠군", "했군", "하군", "이군", "로군", "더군", "다고", "라고", "냐고",
    };

    // A quotation is someone else's words: "\"살려 주세요!\" 하고 울부짖더군." is said in 반말.
    private static readonly System.Text.RegularExpressions.Regex Quoted = new(
        "\"[^\"]*\"|“[^”]*”|‘[^’]*’|「[^」]*」|『[^』]*』", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static ToneKind SpokenLevel(string? text)
    {
        var cleaned = LqaScanner.StripUiTokens(text ?? "").Trim().TrimEnd(' ', '.', ',', '!', '?', '…', '"', '\'', '”', '’', ')', '~', '-');
        // A closing vocative is no ending: "무슨 일로 오셨습니까, 친구여" is judged by "오셨습니까".
        var comma = cleaned.LastIndexOf(',');
        if (comma > 0 && !cleaned[(comma + 1)..].Trim().Contains(' '))
        {
            var clause = cleaned[..comma];
            var tone = LqaToneClassifier.Classify(clause);
            if (tone != ToneKind.Unknown && tone != ToneKind.PlainDa)
            {
                return tone == ToneKind.Casual ? ToneKind.Casual : ToneKind.Hamnida;
            }

            cleaned = clause.TrimEnd();
        }

        if (cleaned.Length == 0 || !cleaned.Contains(' ') && cleaned.Length <= 2)
        {
            return ToneKind.Unknown; // "Mercy!" as "자비를!" or a lone name says nothing about the level.
        }

        if (cleaned.EndsWith("죠", StringComparison.Ordinal))
        {
            return ToneKind.Hamnida;
        }

        return CasualEndings.Any(e => cleaned.EndsWith(e, StringComparison.Ordinal)) ? ToneKind.Casual : ToneKind.Unknown;
    }

    private static string Display(ToneKind level) => level == ToneKind.Hamnida ? "존댓말" : "반말";
}
