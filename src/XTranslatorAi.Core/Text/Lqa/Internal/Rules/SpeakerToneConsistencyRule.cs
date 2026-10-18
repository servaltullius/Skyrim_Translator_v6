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
        // Quotations and stage directions are not the speaker's own words; a line that is nothing but those is judged
        // as it is.
        var original = text ?? "";
        var spoken = Quoted.Replace(StageDirection.Replace(original, " "), " ").Trim();
        var stripped = false;
        if (spoken.Any(char.IsLetter))
        {
            stripped = !string.Equals(spoken, original.Trim(), StringComparison.Ordinal);
            text = spoken;
        }

        var parts = (text ?? "").Split(new[] { '.', '!', '?', '…', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cries = Cries(parts);
        if (parts.Length > 1 && parts.Any(part => !cries.Contains(part) && SentenceLevel(part, keepShort: false) == ToneKind.Hamnida))
        {
            return ToneKind.Hamnida;
        }

        // A short reply left by a stage direction ("*한숨* 좋아.") is a reply, not a lone name.
        var level = SentenceLevel(text, keepShort: stripped);
        if (level == ToneKind.Unknown && EndsInOpenClause(text))
        {
            // "누가 알겠어. 그 안에 뭔가 비싼 게 들어있을지.": the open clause trails a sentence that has the level, the
            // nearest one before it that has one ("잘 있어, 엔써... 뭐라 감사의 말을 해야 할지...").
            level = parts.Reverse().Skip(1)
                .Where(part => !EndsInOpenClause(part) && !cries.Contains(part))
                .Select(part => SentenceLevel(part, keepShort: false))
                .FirstOrDefault(found => found != ToneKind.Unknown);
        }

        return level;
    }

    /// <summary>
    /// The short words a line repeats as a cry: Serana's "포요! 포요!" (a creature's noise) made her 반말 line count as
    /// 해요체. A short word said once ("뭐요? 그게 무슨 뜻이오?", "저요! 저!") is a real 해요체 reply, and so is one said
    /// twice that has a 해요체 shape ("가요! 가요! 빨리!").
    /// </summary>
    private static HashSet<string> Cries(string[] parts)
        => parts.Where(part => part.Length <= 2 && !part.Any(char.IsWhiteSpace) && !IsShortHaeyo(part))
            .GroupBy(part => part, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A two-syllable word in 요 that 해요체 makes: a stem fused with 아/어 (가요, 봐요, 줘요, 돼요, 해요, 서요), a pronoun
    /// (뭐요, 왜요, 저요, 나요) or a particle (또요, 고요, 도요). 포요 or 뿌요 is none of these.
    /// </summary>
    private static bool IsShortHaeyo(string word)
    {
        if (word.Length != 2 || word[1] != '요' || !KoreanSyllables.IsHangulSyllable(word[0]) || KoreanSyllables.HasFinalConsonant(word[0]))
        {
            return false;
        }

        // ㅏ ㅐ ㅓ ㅔ ㅕ ㅖ ㅘ ㅙ ㅚ ㅝ ㅞ, the vowels a 해요체 stem ends in.
        var medial = (word[0] - '가') / 28 % 21;
        return medial is 0 or 1 or 4 or 5 or 6 or 7 or 9 or 10 or 11 or 14 or 15 || word[0] is '또' or '고' or '도';
    }

    /// <summary>
    /// 존댓말 (Hamnida) or 반말 (Casual) by the last sentence's ending; a lone word in 다 is usually a name, unless
    /// keepShort says the line had more (a stage direction or a quotation) and the word is the reply.
    /// </summary>
    private static ToneKind SentenceLevel(string? text, bool keepShort)
    {
        var tone = LqaToneClassifier.Classify(text);
        if (tone == ToneKind.Casual && EndsInOpenClause(text))
        {
            tone = ToneKind.Unknown;
        }
        var level = tone switch
        {
            ToneKind.Hamnida or ToneKind.Haeyo => ToneKind.Hamnida,
            ToneKind.Casual => ToneKind.Casual,
            ToneKind.PlainDa => keepShort || LqaScanner.StripUiTokens(text ?? "").Trim().Any(char.IsWhiteSpace) ? ToneKind.Casual : ToneKind.Unknown,
            _ => ToneKind.Unknown,
        };
        return level == ToneKind.Unknown ? SpokenLevel(text, keepShort) : level;
    }

    // "야를님, 이걸 어떻게 말씀드려야 할지...", "어떻게 될지", "왜 그런지": this 지 asks "whether" in a clause left open,
    // and is not 반말's 지. A polite line that trails off so was counted as 반말 among Serana's lines to the Jarl.
    private static readonly string[] OpenClauseEndings =
    {
        "할지", "될지", "을지", "는지", "은지", "인지", "던지", "런지", "올지", "갈지", "볼지", "낼지", "릴지",
    };

    private static bool EndsInOpenClause(string? text)
    {
        var cleaned = LqaScanner.StripUiTokens(text ?? "").Trim().TrimEnd(' ', '.', ',', '!', '?', '…', '"', '\'', '”', '’', ')', '~', '-');
        return OpenClauseEndings.Any(ending => cleaned.EndsWith(ending, StringComparison.Ordinal));
    }

    // The shared classifier knows 반말 only by 해/야/지/냐, so "훌륭했어!", "다시 와.", "덤벼 봐." and "물론이죠." had no
    // level and a speaker's odd line passed. Dialogue lines only (item text is classified by the shared rules). Not 네
    // (the polite "yes"), 게, 데 or 래, which end nouns as often as 반말 ("여긴 내 가게.", "그런데...", "노래.").
    private static readonly string[] CasualEndings =
    {
        "어", "아", "봐", "줘", "와", "워", "돼", "걸", "구나", "렴",
        "는군", "었군", "았군", "겠군", "했군", "하군", "이군", "로군", "더군", "없군", "있군", "다고", "라고", "냐고",
    };

    // A quotation is someone else's words: "\"살려 주세요!\" 하고 울부짖더군." is said in 반말, and Serana's "아, 속편이네.
    // '내 창을 좀 닦아 주시겠어요, 내 사랑?'" reads a book's line. Single quotes mark a quotation only around a
    // sentence (with its own . ! ? or …) and where no letter is before the first, so neither the apostrophe in a name
    // (J'zargo, 지'자르고) nor an echoed word in the speaker's own sentence ("문학의 '학자가 아니다'?") is taken for one.
    private static readonly System.Text.RegularExpressions.Regex Quoted = new(
        "\"[^\"]*\"|“[^”]*”|‘[^’]*’|「[^」]*」|『[^』]*』|(?<![\\p{L}\\p{N}])'[^'\\n]*[.!?…][^'\\n]*'",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // A bare cry ("와아...", "아!", "으아") is no reply: when a quotation or stage direction is taken away, it is still
    // not 반말 for ending in 아 or 어.
    private static readonly System.Text.RegularExpressions.Regex Interjection = new(
        "^[아어오와우으에하허흐후휴음흠헉윽악앗엇]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // A stage direction between asterisks ("*웃음*", "*그 모드에 나오는 버섯*") is narration, not speech.
    private static readonly System.Text.RegularExpressions.Regex StageDirection = new(
        "\\*[^*\\n]+\\*", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static ToneKind SpokenLevel(string? text, bool keepShort)
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

        if (cleaned.Length == 0 || !cleaned.Contains(' ') && (Interjection.IsMatch(cleaned) || !keepShort && cleaned.Length <= 2))
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
