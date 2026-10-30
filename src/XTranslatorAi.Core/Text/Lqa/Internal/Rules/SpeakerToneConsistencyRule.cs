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
/// naturally in polite speech, so the levels compared are only 존댓말 and the rest.
/// <para>
/// 하오체 and 하게체 are on the rest's side: in the Korean patch a 하게체 speaker (Tolfdir, Paarthurnax) says "~지",
/// "~군" and "~다" as often as "~네" and "~게", and a 하오체 speaker (Urag, Teldryn Sero, General Tullius) mixes "~소"
/// and "~시오" with "~지", "~군" and "~거요". So what the check catches is 해요체 or 합니다체 in such a voice, and 하오체
/// or 하게체 in a 존댓말 voice; the warning names the style ("대부분 하오체, 이 대사 해요체").
/// </para>
/// <para>
/// A speaker with too few lines for a clear majority, or with the lines split (4 to 3), was not shown at all: in Fists
/// of Fury, Reggr's one 존댓말 line among his 반말 ones passed. Such a split is listed for reference (Info), as the
/// player's lines are: the player's level follows who is spoken to, so the player's lines to one NPC (an INFO's RNAM
/// or a topic's text, whose Speaker is that NPC) keep one level, but lines to different NPCs may differ.
/// </para>
/// <para>This file groups the lines and compares them; how one line reads is in SpeakerToneConsistencyRule.Reading.</para>
/// </summary>
internal static partial class SpeakerToneConsistencyRule
{
    public const string Code = "speaker_tone_inconsistent";
    public const string MixedCode = "speaker_tone_mixed";
    public const string PlayerCode = "player_tone_inconsistent";

    // The player's level to an NPC, as SpeakerStyles keys it ("player>Gian").
    private static readonly string PlayerKeyPrefix = SpeakerStyles.PlayerSpeaker + DialogueSpeakerNames.ListenerSeparator;

    // A scene line said to a character the plugin does not name (PluginSpeakers.UnknownListener, as "%08X").
    private const string UnknownListener = "00000000";

    internal static readonly System.Text.RegularExpressions.Regex SpeechCheck = new(
        @"\((?:Persuade|Intimidate|Bribe|Lie|Brawl|Threaten)[^)]*\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The style of a line: 존댓말 (합니다체, 해요체) on one side, 하오체, 하게체 and 반말 on the other.</summary>
    internal enum Register { Unknown, Polite, Hao, Hage, Casual }

    /// <param name="CasualNames">What the non-존댓말 side of each group mostly is: 반말, 하오체 or 하게체.</param>
    /// <param name="HaoGroups">Groups whose non-존댓말 side is mostly 하오체, where a line that is 존댓말 only by a weak
    /// sign (noun + 요, "~ㅂ시다") is read as 하오체: Teldryn Sero's "다음엔 어디요?" and "그럼 출발합시다!".</param>
    /// <param name="StrayHao">Groups in a plain 반말 voice with a 하오체 line or two, by their 반말 and 하오체 counts:
    /// Brelyna Maryon's one "...생각하는 거요?" among 67 반말 lines.</param>
    /// <param name="Declared">The speech levels decided per speaker and listener (SpeakerStyles): a group with one is
    /// compared with it, not with its own majority. Meridia's Order's Laracin was decided to speak 합쇼체 to the
    /// Champion, and with most of his lines written in 반말 the majority flagged his right lines.</param>
    public sealed record Levels(IReadOnlyDictionary<string, ToneKind> Majorities, IReadOnlyDictionary<string, (int Polite, int Casual)> Counts,
        IReadOnlyDictionary<string, string> CasualNames, IReadOnlySet<string> HaoGroups,
        IReadOnlyDictionary<string, (int Casual, int Hao)> StrayHao,
        IReadOnlyDictionary<(string Speaker, string Listener), string>? Declared = null);

    public static Levels BuildSpeakerMajorities(IReadOnlyList<LqaScanEntry> entries,
        IReadOnlyList<XTranslatorAi.Core.Data.SpeakerStyle>? styles = null)
    {
        var levels = new Dictionary<string, List<(Register Register, bool Weak)>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            // A line whose listener is only the scene's other actor may be said to the player: it is no evidence of
            // either group's level.
            if (Key(entry) is not { } key || IsGuessed(key))
            {
                continue;
            }

            if (!levels.TryGetValue(key, out var list))
            {
                list = new List<(Register, bool)>();
                levels[key] = list;
            }

            list.Add(Level(entry.DestText));
        }

        var majorities = new Dictionary<string, ToneKind>(StringComparer.Ordinal);
        var counts = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var casualNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var haoGroups = new HashSet<string>(StringComparer.Ordinal);
        var strayHao = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var (key, list) in levels)
        {
            var haoGroup = CasualName(list.Select(l => l.Register).ToList()) == Display(Register.Hao);
            var registers = list.Select(l => haoGroup && l.Weak ? Register.Hao : l.Register).ToList();
            var sides = registers.Select(Side).ToList();
            if (!key.StartsWith(PlayerKeyPrefix, StringComparison.Ordinal) && LqaToneClassifier.TryGetStrongMajorityTone(sides, out var majority))
            {
                majorities[key] = majority;
            }

            counts[key] = (sides.Count(l => l == ToneKind.Hamnida), sides.Count(l => l == ToneKind.Casual));
            casualNames[key] = CasualName(registers);
            if (haoGroup)
            {
                haoGroups.Add(key);
            }

            // 하오체 speakers say "~지" and "~군" too, so only a voice that is plainly 반말 with a 하오체 line or two counts.
            var plain = registers.Count(r => r == Register.Casual);
            var hao = registers.Count(r => r == Register.Hao);
            if (plain >= 6 && hao is > 0 and <= 2 && hao * 5 <= plain)
            {
                strayHao[key] = (plain, hao);
            }
        }

        return new Levels(majorities, counts, casualNames, haoGroups, strayHao,
            styles is { Count: > 0 } ? SpeakerStyles.ByPair(styles) : null);
    }

    private static ToneKind Side(Register register) => register switch
    {
        Register.Polite => ToneKind.Hamnida,
        Register.Unknown => ToneKind.Unknown,
        _ => ToneKind.Casual,
    };

    // The style most of the group's non-존댓말 lines are in, when it is at least half of them.
    private static string CasualName(IReadOnlyList<Register> registers)
    {
        var casual = registers.Where(r => Side(r) == ToneKind.Casual).ToList();
        var top = casual.GroupBy(r => r).OrderByDescending(g => g.Count()).FirstOrDefault();
        return top != null && top.Count() * 2 >= casual.Count ? Display(top.Key) : "반말";
    }

    public static void Apply(LqaScanEntry entry, Levels levels, List<LqaIssue> issues)
    {
        if (Key(entry) is not { } key)
        {
            ApplyDecidedToSpeechCheck(entry, levels, issues);
            return;
        }

        var (register, weak) = Level(entry.DestText);
        // A guessed line is in no group of its own: it goes by the groups it may belong to.
        var haoKey = IsGuessed(key) ? key[..^1] : key;
        var separator = haoKey.IndexOf(DialogueSpeakerNames.ListenerSeparator);
        var haoSpeaker = separator < 0 ? haoKey : haoKey[..separator];
        var declared = levels.Declared is { } decided ? SpeakerStyles.For(decided, key) : null;
        if (weak && register == Register.Polite && (levels.HaoGroups.Contains(haoKey) || IsGuessed(key) && levels.HaoGroups.Contains(haoSpeaker)
                                                    || declared is "하오체" or "하게체"))
        {
            register = Register.Hao;
        }

        var level = Side(register);
        if (level == ToneKind.Unknown)
        {
            return;
        }

        if (IsGuessed(key))
        {
            ApplyGuessed(entry, key, register, level, levels, issues);
            // Decided levels to both it may be said to, on the same side as the line but another level: Alzacar, decided
            // 반말 to the player and the Jester, said "챔피언, 그대의 뜻을 돕겠소."
            if (levels.Declared is { } both && SpeakerStyles.For(both, haoKey) is { } toListener && SpeakerStyles.For(both, haoSpeaker) is { } toPlayer
                && (level == ToneKind.Hamnida) == SpeakerStyles.IsPolite(toListener) && (level == ToneKind.Hamnida) == SpeakerStyles.IsPolite(toPlayer)
                && StrayFromDeclared(entry.DestText, toListener) is { } shown && StrayFromDeclared(entry.DestText, toPlayer) != null)
            {
                issues.Add(Issue(entry, "Info", MixedCode,
                    $"정한 말투(상대에게 {toListener}, 플레이어에게 {toPlayer})와 다를 수 있습니다: 이 대사 {shown}"));
            }

            return;
        }

        if (declared != null)
        {
            var whom = key.Contains(DialogueSpeakerNames.ListenerSeparator) ? "이 상대에게" : "플레이어에게";
            var playerLine = key.StartsWith(PlayerKeyPrefix, StringComparison.Ordinal);
            if ((level == ToneKind.Hamnida) != SpeakerStyles.IsPolite(declared))
            {
                issues.Add(Issue(entry, "Warn", playerLine ? PlayerCode : Code,
                    $"{whom} 정한 말투와 다릅니다: 정한 말투 {declared}, 이 대사 {Display(register)}"));
            }
            else if (StrayFromDeclared(entry.DestText, declared) is { } shown)
            {
                issues.Add(Issue(entry, "Info", playerLine ? PlayerCode : MixedCode,
                    $"{whom} 정한 말투와 다를 수 있습니다: 정한 말투 {declared}, 이 대사 {shown}"));
            }

            return;
        }

        var casualName = levels.CasualNames[key];

        var player = key.StartsWith(PlayerKeyPrefix, StringComparison.Ordinal);
        var who = key.EndsWith(DialogueSpeakerNames.ListenerSeparator + UnknownListener, StringComparison.Ordinal) ? "같은 화자가 다른 인물(장면 상대, 전투 중의 적)에게 한"
            : key.Contains(DialogueSpeakerNames.ListenerSeparator) ? "같은 화자가 같은 상대에게 한"
            : "같은 화자의";
        if (!player && register == Register.Hao && levels.StrayHao.TryGetValue(key, out var stray)
            && (!levels.Majorities.TryGetValue(key, out var side) || side == ToneKind.Casual))
        {
            issues.Add(Issue(entry, "Info", MixedCode, $"{who} 대사끼리 말투가 갈립니다(반말 {stray.Casual}줄, 하오체 {stray.Hao}줄, 이 줄 하오체)"));
            return;
        }

        if (!player && levels.Majorities.TryGetValue(key, out var majority))
        {
            if (level != majority)
            {
                var most = majority == ToneKind.Hamnida ? "존댓말" : casualName;
                issues.Add(Issue(entry, "Warn", Code, $"{who} 다른 대사와 말투가 다릅니다: 대부분 {most}, 이 대사 {Display(register)}"));
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

        var counted = $"존댓말 {polite}줄, {casualName} {casual}줄, 이 줄 {Display(register)}";
        issues.Add(player
            ? Issue(entry, "Info", PlayerCode, $"같은 상대에게 하는 플레이어 선택지끼리 말투가 다릅니다({counted})")
            : Issue(entry, "Info", MixedCode, $"{who} 대사끼리 말투가 갈립니다({counted})"));
    }

    private static bool IsGuessed(string key) => key.EndsWith(DialogueSpeakerNames.GuessedListenerMark);

    /// <summary>
    /// A scene line whose listener is a guess (the scene's other actor; it may be said to the player) is flagged only
    /// when it differs from both: the speaker's lines to that actor and the speaker's lines to the player. In Meridia's
    /// Order, Alara's "Champion, meet us at Fort Justice." was counted among her lines to Marith.
    /// </summary>
    private static void ApplyGuessed(LqaScanEntry entry, string key, Register register, ToneKind level, Levels levels, List<LqaIssue> issues)
    {
        var listenerKey = key[..^1];
        var speakerKey = listenerKey[..listenerKey.IndexOf(DialogueSpeakerNames.ListenerSeparator)];
        if (Dominant(listenerKey) is not { } toListener || Dominant(speakerKey) is not { } toPlayer || toListener == level || toPlayer == level)
        {
            return;
        }

        // A level decided for the pair (SpeakerStyles) comes first, as in Dominant, and gives a majority with no line of the
        // pair counted, so the pair may have no name among the counted groups: indexing the names stopped the whole check.
        var most = levels.Declared is { } declared && SpeakerStyles.For(declared, listenerKey) is { } decided ? "정한 말투 " + decided
            : toListener == ToneKind.Hamnida ? "대부분 존댓말"
            : "대부분 " + (levels.CasualNames.TryGetValue(listenerKey, out var name) ? name : "반말");
        issues.Add(Issue(entry, "Warn", Code,
            $"장면에서 상대를 짐작한 대사인데, 그 상대에게 한 대사와도 플레이어에게 한 대사와도 말투가 다릅니다: {most}, 이 대사 {Display(register)}"));

        // The side most of a group is on (a group all on one side has no majority entry, the guessed line not being in it),
        // or the decided level's.
        ToneKind? Dominant(string group)
        {
            if (levels.Declared is { } decided && SpeakerStyles.For(decided, group) is { } style)
            {
                return SpeakerStyles.IsPolite(style) ? ToneKind.Hamnida : ToneKind.Casual;
            }

            if (!levels.Counts.TryGetValue(group, out var count) || count.Polite + count.Casual < 4) return null;
            var (side, n) = count.Polite >= count.Casual ? (ToneKind.Hamnida, count.Polite) : (ToneKind.Casual, count.Casual);
            return n * 4 >= (count.Polite + count.Casual) * 3 ? side : null;
        }
    }

    // A threat takes its own attitude; a persuasion, a bribe or a lie keeps the player's level to the NPC.
    private static readonly System.Text.RegularExpressions.Regex Threat = new(
        @"\((?:Intimidate|Brawl|Threaten)[^)]*\)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A speech-check choice is in no group, but a level decided for the player to that NPC still holds for a persuasion,
    /// a bribe or a lie: in Meridia's Order the player, decided 해요체 to Alzacar, persuaded him in 반말 ("그녀가 널 어떻게
    /// 대하는지 봐. 뻔하잖아. (설득)").
    /// </summary>
    private static void ApplyDecidedToSpeechCheck(LqaScanEntry entry, Levels levels, List<LqaIssue> issues)
    {
        if (levels.Declared is not { } decided || !TranslationRowRules.IsPlayerLine(entry.Rec) || string.IsNullOrWhiteSpace(entry.Speaker)
            || entry.Status is not (StringEntryStatus.Done or StringEntryStatus.Edited)
            || !SpeechCheck.IsMatch(entry.SourceText ?? "") || Threat.IsMatch(entry.SourceText ?? "")
            || SpeakerStyles.For(decided, PlayerKeyPrefix + entry.Speaker) is not { } declared)
        {
            return;
        }

        var (register, _) = Level(entry.DestText);
        var level = Side(register);
        if (level == ToneKind.Unknown)
        {
            return;
        }

        if ((level == ToneKind.Hamnida) != SpeakerStyles.IsPolite(declared))
        {
            issues.Add(Issue(entry, "Warn", PlayerCode, $"이 상대에게 정한 말투와 다릅니다: 정한 말투 {declared}, 이 대사 {Display(register)}"));
        }
        else if (StrayFromDeclared(entry.DestText, declared) is { } shown)
        {
            issues.Add(Issue(entry, "Info", PlayerCode, $"이 상대에게 정한 말투와 다를 수 있습니다: 정한 말투 {declared}, 이 대사 {shown}"));
        }
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
    /// does not name is "00000000", lines to someone other than the player, and "@jarl" a title the line calls, see
    /// DialogueAddressees); the player's lines
    /// (INFO:RNAM, DIAL:FULL) by the NPC they are said to. Null for a line with no known speaker or not translated yet.
    /// </summary>
    private static string? Key(LqaScanEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Speaker) || entry.Status is not (StringEntryStatus.Done or StringEntryStatus.Edited))
        {
            return null;
        }

        if (TranslationRowRules.IsPlayerLine(entry.Rec))
        {
            // A speech-check choice ((Intimidate), (Brawl) ...) takes its own attitude, apart from the player's others.
            return SpeechCheck.IsMatch(entry.SourceText ?? "") ? null : PlayerKeyPrefix + entry.Speaker;
        }

        return TranslationRowRules.IsNpcLine(entry.Rec) ? entry.Speaker : null;
    }

    private static string Display(Register register) => register switch
    {
        Register.Polite => "존댓말",
        Register.Hao => "하오체",
        Register.Hage => "하게체",
        _ => "반말",
    };
}
