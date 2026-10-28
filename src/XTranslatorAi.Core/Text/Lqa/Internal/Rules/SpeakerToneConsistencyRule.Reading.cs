using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// How a line of <see cref="SpeakerToneConsistencyRule"/> reads: its level (존댓말, 하오체, 하게체 or 반말) from its
/// sentences' endings, with quotations, stage directions and cries left out, and whether its 존댓말 is only a weak sign
/// a 하오체 speaker shares (a noun with 요, ~ㄹ 게요, ~ㅂ시다). The groups and what is compared are in the main file.
/// </summary>
internal static partial class SpeakerToneConsistencyRule
{
    private static readonly System.Text.RegularExpressions.Regex FormalEnding = new(@"(니다|니까)[\s.!?…""']*$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The speech level a base game NPC speaks in the Korean patch, read from its lines there; null when they are too
    /// few or mixed. A 하오체 speaker mixes ~지 and ~군 that read as 반말 (Teldryn Sero: 다음엔 어디요?, ...좋겠군), so a
    /// quarter of the plain lines in 하오체 makes one; a 반말 speaker's stray 하오체 line or two does not.
    /// In the Teldryn comparison the GUI wrote him in 반말 and the agent mode's Gemini Flash in 해요체, where the patch
    /// and the reviewed release have 하오체.
    /// </summary>
    internal static string? PatchLevel(IEnumerable<string> lines)
    {
        int polite = 0, formal = 0, hao = 0, hage = 0, casual = 0;
        foreach (var line in lines)
        {
            var (register, weak) = Level(line);
            if (weak)
            {
                continue;
            }

            switch (register)
            {
                case Register.Polite:
                    polite++;
                    if (FormalEnding.IsMatch(line)) formal++;
                    break;
                case Register.Hao: hao++; break;
                case Register.Hage: hage++; break;
                case Register.Casual: casual++; break;
            }
        }

        var total = polite + hao + hage + casual;
        if (total < 8)
        {
            return null;
        }

        if (polite >= total * 0.6)
        {
            return formal * 2 >= polite ? "합쇼체" : "해요체";
        }

        var plain = hao + hage + casual;
        if (hao >= 3 && hao >= plain * 0.25 && hao >= hage)
        {
            return "하오체";
        }

        if (hage >= 3 && hage >= plain * 0.25)
        {
            return "하게체";
        }

        return casual >= total * 0.6 ? "반말" : null;
    }

    /// <summary>Whether two versions of a line read at the same known level, as this rule reads them (LqaReviewedMarks).</summary>
    internal static bool SameLevel(string? before, string? after)
    {
        var level = Level(before);
        return level.Register != Register.Unknown && level == Level(after);
    }

    internal enum HaoReading { None, Hao, PoliteBeside }

    /// <summary>
    /// Whether a sentence of the line ends in this rule's 하오체 ("무슨 뜻이오?", "괜찮소?", "…있는거요?";
    /// DialogueToneConsistencyRule). A 하오체 speaker closes some sentences with ~지 or ~군 ("…통치하지 않소. 이제는
    /// … 들어갔지."), so one 하오체 sentence makes the line 하오체. A lone 거요 counts after ~ㄹ or ~는 only ("할 거요",
    /// "하는 거요"): "대화거리 같은 거요" is 해요체.
    /// "~ㄹ 거요", "~는 거요" and "~ㄴ 게요" are 해요체 as well ("절 곁에 두는 거요. … 찾아야 해요."): when they are the only
    /// 하오체 sign and another sentence is plainly 존댓말, the line is 해요체 (<see cref="HaoReading.PoliteBeside"/>), whatever
    /// the order of its sentences (eight 해요체 lines of the user's projects were read as 하오체, and "고마워요. 그건 내가 할
    /// 거요." as nothing). Quotations are not the speaker's, and a sentence is polite as <see cref="Level"/> reads it (~죠
    /// included).
    /// </summary>
    internal static HaoReading ReadHao(string? text)
    {
        var spoken = SpokenPart(text);
        var parts = spoken.Split(new[] { '.', '!', '?', '…', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => Clean(part).Length > 0).ToList();
        var hao = parts.Where(part => Clean(part) is var sentence && !sentence.EndsWith("십시오", StringComparison.Ordinal) && IsHao(sentence)
                                      && !(LastWord(sentence) == "거요" && !AfterModifierOfHao(sentence))).ToList();
        if (hao.Count == 0)
        {
            return HaoReading.None;
        }

        static bool SharedWithHaeyo(string part) => LastWord(Clean(part)) is var word && (word.EndsWith("거요", StringComparison.Ordinal) || word == "게요");
        return hao.All(SharedWithHaeyo)
               && parts.Any(part => !hao.Contains(part) && SentenceLevel(part, keepShort: false) == Register.Polite && !IsWeakPolite(Clean(part)))
            ? HaoReading.PoliteBeside
            : HaoReading.Hao;
    }

    /// <summary>
    /// Whether the line is 존댓말 by a sentence of its own, as <see cref="Level"/> reads the whole line: "고마워요. 갑시다." and
    /// "정말 고마워요. 이건 제 열쇠요." are, while "그럼 출발합시다!", "당신은 누구요?" are 존댓말 only by signs 하오체 shares
    /// (see IsWeakPolite), and "또 오셨나? 이번엔 어디요?" or "준비는 된 거요?" read as 하게체 or 하오체.
    /// </summary>
    internal static bool HasOwnPolite(string? text) => Level(text) == (Register.Polite, false);

    /// <summary>The line without its quotations (someone else's words), or the line itself when it is nothing but those.</summary>
    internal static string SpokenPart(string? text)
    {
        var spoken = Quoted.Replace(text ?? "", " ");
        return spoken.Any(char.IsLetter) ? spoken : text ?? "";
    }

    private static bool AfterModifierOfHao(string sentence)
    {
        var before = sentence[..^2].TrimEnd();
        return before.Length > 0 && (before[^1] == '는' || FinalConsonant(before[^1]) == 8);
    }

    /// <summary>
    /// 존댓말 when any sentence or clause of the line is: "어디 한번 보죠. 당신이 어떤 실력인지." and "물론이죠, 돈만
    /// 있다면야." are polite lines that trail off. Otherwise the last sentence's level.
    /// </summary>
    private static (Register Register, bool Weak) Level(string? text)
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
        var pieces = parts.Where(part => !cries.Contains(part)).Select(part => (Part: part, Register: SentenceLevel(part, keepShort: false))).ToList();
        // A 하오체 or 하게체 piece outweighs a weak sign of 존댓말 in the same line: "…좋소. 가져가시오. 여기 열쇠요."
        var styled = pieces.LastOrDefault(piece => piece.Register is Register.Hao or Register.Hage).Register;
        if (parts.Length > 1)
        {
            if (pieces.Any(piece => piece.Register == Register.Polite && !IsWeakPolite(piece.Part)))
            {
                return (Register.Polite, false);
            }

            if (styled == Register.Unknown && pieces.Any(piece => piece.Register == Register.Polite))
            {
                return (Register.Polite, true);
            }
        }

        // A short reply left by a stage direction ("*한숨* 좋아.") is a reply, not a lone name.
        var level = SentenceLevel(text, keepShort: stripped);
        var weak = level == Register.Polite && IsWeakPolite(text);
        if (weak && styled != Register.Unknown)
        {
            (level, weak) = (styled, false);
        }

        if (level == Register.Unknown)
        {
            // "누가 알겠어. 그 안에 뭔가 비싼 게 들어있을지.", "여기서 나가시오, 화를 내기 전에.": a last clause with no
            // level trails a sentence that has one, the nearest before it ("잘 있어, 엔써... 뭐라 감사의 말을 해야 할지...").
            var before = parts.Reverse().Skip(1)
                .Where(part => !EndsInOpenClause(part) && !cries.Contains(part))
                .Select(part => (Part: part, Register: SentenceLevel(part, keepShort: false)))
                .FirstOrDefault(found => found.Register != Register.Unknown);
            level = before.Register;
            // "좋은 생각이오, 조용히 갑시다. 드로거는 귀가 밝으니까.": the nearest piece is a weak sign of 존댓말, and the
            // line's 하오체 piece decides, as it does for a last sentence (Teldryn in Extended Hireling Dialogue).
            if (level == Register.Polite && IsWeakPolite(before.Part))
            {
                (level, weak) = styled != Register.Unknown ? (styled, false) : (level, true);
            }
            // A line that only trails off, with no sentence before it to go by: "분명 더 쓸모 있는 일을 할 수 있을
            // 텐데, 챔피언.", "이건 믿음이 안 가." (Laracin in Meridia's Order). 존댓말 would end in 요 (텐데요).
            if (level == Register.Unknown && EndsInCasualClause(text))
            {
                level = Register.Casual;
            }
        }

        return (level, weak);
    }

    /// <summary>
    /// 존댓말 by a sign 하오체 shares: a noun with 요 ("여기 열쇠요", "멋진 변화요", "어디요", "뭐요"), "~ㄹ 게요" and
    /// "~ㅂ시다" ("갑시다"). Among 존댓말 speakers these are 존댓말 ("말로요", "많이요", "갑시다" alike), so they stay so
    /// unless the line or the speaker is 하오체.
    /// </summary>
    private static bool IsWeakPolite(string? text)
    {
        var cleaned = Clean(text);
        var word = LastWord(cleaned);
        if (cleaned.Length >= 3 && cleaned.EndsWith("시다", StringComparison.Ordinal) && FinalConsonant(cleaned[^3]) == 17)
        {
            return true;
        }

        if (word is "게요" or "뭐요" or "어디요" or "누구요" or "언제요")
        {
            return true;
        }

        // The noun 자 (person) after a modifier, in 하오체: "리프튼 하수도에서 지내던 시절에 알던 자요", "믿을 만한 자요".
        if (word == "자요" && cleaned.Length > 3 && cleaned[^3] == ' ' && FinalConsonant(cleaned[^4]) == 4)
        {
            return true;
        }

        // A 요 that no 해요체 ending makes is a noun's: 열쇠요, 변화요, 전부요, 의무요, 다요 (not 좋아요, 그래요, 세요).
        return word.Length >= 2 && word[^1] == '요' && word != "아니요"
               && (!HaeyoEndings.Contains(word[^2..]) || IsNounLookingLikeHaeyo(word) || IsNounWithHaeyoShape(word));
    }

    /// <summary>
    /// A noun with 요 whose last two syllables are also a 해요체 ending, as the Korean patch has them: "문서요" (서요 as in
    /// 일어서요, 그래서요), "동반자요" and "장기숙박자요" (자요, sleep), and 하오체 "낼꺼요" (꺼요, turn off). The 해요체
    /// forms need a stem fused with 아/어 before 서 (그래서요, 같아서요, 일어서요: no final consonant), 자다's few
    /// compounds are short (자요, 잠자요), and 꺼요 after a final ㄹ is the 하오체 거요 ("~ㄹ 거요").
    /// </summary>
    private static bool IsNounLookingLikeHaeyo(string word)
    {
        if (word.Length < 3 || !KoreanSyllables.IsHangulSyllable(word[^3]))
        {
            return false;
        }

        return word[^2..] switch
        {
            "서요" => KoreanSyllables.HasFinalConsonant(word[^3]),
            "자요" => word.Length >= 4,
            "꺼요" => FinalConsonant(word[^3]) == 8,
            _ => false,
        };
    }

    /// <summary>
    /// A noun with 요 whose last two syllables 해요체 also ends in, the 하오체 "N이오" of a vowel-final noun as Teldryn's
    /// lines have it: 짐승 같은 사내요, 딱한 친구요, 이상한 여자요, 나도 마찬가지요, 공정한 거래요. The 해요체 forms
    /// these endings are also made of stay strong: 지내요, 보내요, 그래요, 듣지요, 리스너라구요, 있나요, 하나요.
    /// </summary>
    private static bool IsNounWithHaeyoShape(string word)
    {
        if (word.Length < 3 || !KoreanSyllables.IsHangulSyllable(word[^3]))
        {
            return false;
        }

        return word[^2..] switch
        {
            "내요" => word[^3] is not ('지' or '보' or '꺼' or '드' or '해' or '러' or '뎌' or '려'),
            "자요" => true,
            // Not 다구요, 라구요, 말구요, 하구요 (Cicero's "선생이 리스너라구요!").
            "구요" => word[^3] is not ('다' or '라' or '말' or '하' or '고' or '서'),
            // Not the questions 있나요, 하나요, 되나요, 가나요.
            "나요" => FinalConsonant(word[^3]) != 20 && word[^3] is not ('하' or '되' or '가' or '오' or '보' or '주' or '시' or '는' or '었' or '았'),
            // Not 할래요, 갈래요 (a final ㄹ), nor 그래요, 했대요.
            "래요" => word is not ("그래요" or "이래요" or "저래요") && word[^3] is not ('대' or '했' or '겠') && FinalConsonant(word[^3]) != 8,
            // 지요 is 해요체's own ending after any stem (듣지요, 화신이시지요); only the noun 마찬가지.
            "지요" => word == "마찬가지요",
            // Teldryn's 하오체 "소문은 진짜요", "허위 광고요": the nouns 진짜, 가짜, 광고, 최고, 경고, 창고, not 짜요 (짜다)
            // nor ~고요 after a verb's stem (하고요, 있고요, 먹었고요, 학생이고요).
            "짜요" => word is "진짜요" or "가짜요",
            "고요" => word[^3] is '광' or '최' or '경' or '창' or '금' or '원' or '재' or '참' or '선',
            _ => false,
        };
    }

    // The last two syllables of 해요체 and its endings in 요: a stem fused with 아/어 (가요, 봐요, 줘요, 몰라요, 써요) and
    // the endings 세요, 지요, 군요, 네요, 데요, 래요, 까요, 나요, 걸요, 든요, 대요, 고요, 구요, 서요, 게요.
    private static readonly HashSet<string> HaeyoEndings = new(StringComparer.Ordinal)
    {
        "세요", "셔요", "지요", "군요", "네요", "데요", "래요", "까요", "나요", "걸요", "든요", "대요", "고요", "구요", "서요", "게요",
        "어요", "아요", "여요", "해요", "예요", "에요", "워요", "와요", "봐요", "줘요", "돼요", "되요", "왜요", "라요", "러요",
        "려요", "져요", "쳐요", "켜요", "펴요", "겨요", "혀요", "커요", "써요", "떠요", "꺼요", "터요", "퍼요", "버요", "가요",
        "사요", "자요", "타요", "차요", "파요", "싸요", "짜요", "빠요", "따요", "내요", "새요", "매요", "채요", "태요", "패요",
        "둬요", "꿔요", "춰요", "눠요", "놔요", "뵈요", "죠요",
    };

    // ㅏ ㅐ ㅓ ㅔ ㅕ ㅖ ㅘ ㅙ ㅚ ㅝ ㅞ, the vowels a 해요체 stem ends in before 요.
    private static bool HasHaeyoVowel(char syllable)
        => KoreanSyllables.IsHangulSyllable(syllable) && ((syllable - '가') / 28 % 21) is 0 or 1 or 4 or 5 or 6 or 7 or 9 or 10 or 11 or 14 or 15;

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

        return HasHaeyoVowel(word[0]) || word[0] is '또' or '고' or '도';
    }

    /// <summary>
    /// The style of the last sentence's ending; a lone word in 다 is usually a name, unless keepShort says the line had
    /// more (a stage direction or a quotation) and the word is the reply.
    /// </summary>
    private static Register SentenceLevel(string? text, bool keepShort)
    {
        var register = Ending(text, out var plainDa);
        if (register == Register.Casual && !plainDa && EndsInOpenClause(text))
        {
            register = Register.Unknown;
        }
        else if (plainDa && !keepShort && !LqaScanner.StripUiTokens(text ?? "").Trim().Any(char.IsWhiteSpace))
        {
            register = Register.Unknown;
        }

        return register == Register.Unknown ? SpokenLevel(text, keepShort) : register;
    }

    /// <summary>
    /// The style a sentence's ending marks: this rule's 하오체 and 하게체 endings first (the shared classifier takes
    /// "~시오" for 존댓말 and knows neither), then the shared classifier's. A 해라체 "…다" is 반말 with plainDa set.
    /// </summary>
    private static Register Ending(string? text, out bool plainDa)
    {
        plainDa = false;
        var cleaned = Clean(text);
        if (cleaned.Length == 0)
        {
            return Register.Unknown;
        }

        if (cleaned.EndsWith("십시오", StringComparison.Ordinal))
        {
            return Register.Polite;
        }

        if (IsHao(cleaned))
        {
            return Register.Hao;
        }

        if (IsHage(cleaned))
        {
            return Register.Hage;
        }

        switch (LqaToneClassifier.Classify(text))
        {
            case ToneKind.Hamnida or ToneKind.Haeyo:
                return Register.Polite;
            case ToneKind.Casual:
                return Register.Casual;
            case ToneKind.PlainDa:
                plainDa = true;
                return Register.Casual;
            default:
                return Register.Unknown;
        }
    }

    private static string Clean(string? text)
        => LqaScanner.StripUiTokens(text ?? "").Trim().TrimEnd(' ', '.', ',', '!', '?', '…', '"', '\'', '”', '’', ')', '~', '-');

    /// <summary>
    /// 하오체: "~시오" (not "~십시오"), "~소" after a stem that ends in a consonant 하오체 takes (있소, 없소, 했소, 겠소,
    /// 그렇소, 같소, 않소), "~오" after 하, 이, 되, 마 or 주 (하오, 이오, 되오), "~구려", "~외다", "~소이다", "~ㄹ 거요"
    /// (spaced or not) and "~ㄴ 게요".
    /// </summary>
    private static bool IsHao(string cleaned)
    {
        var word = LastWord(cleaned);
        // ~구려 and ~외다 end nouns and 반말 as well: 고구려, 싸구려 and 구리다's "진짜 구려", and 의외다, 예외다 (a noun with
        // 다). A 반말 group's "네가 여기 있다니 의외다." was marked 하오체. 하오체's own ~외다 follows many stems
        // ("장사꾼이외다", "영주님이시외다", "기쁘외다"), so the nouns are left out rather than the stems listed.
        if (cleaned.EndsWith("시오", StringComparison.Ordinal)
            || cleaned.EndsWith("구려", StringComparison.Ordinal) && word != "구려"
               && !word.EndsWith("고구려", StringComparison.Ordinal) && !word.EndsWith("싸구려", StringComparison.Ordinal)
            || cleaned.EndsWith("외다", StringComparison.Ordinal) && !OeNouns.Any(noun => word.EndsWith(noun + "다", StringComparison.Ordinal))
            || cleaned.EndsWith("소이다", StringComparison.Ordinal) || word == "거요")
        {
            return true;
        }

        // "~ㄹ거요" and "~는거요" written together (찢어발길거요, 하고 있는거요), not 이거요 or 그거요; "~ㄴ 게요" (따라간 게요).
        if (word.Length >= 3 && word.EndsWith("거요", StringComparison.Ordinal) && FinalConsonant(word[^3]) is 4 or 8)
        {
            return true;
        }

        if (word == "게요" && cleaned.Length > 3)
        {
            var previous = cleaned[..^2].TrimEnd();
            if (previous.Length > 0 && FinalConsonant(previous[^1]) == 4)
            {
                return true;
            }
        }

        if (cleaned.Length < 2)
        {
            return false;
        }

        var before = cleaned[^2];
        return cleaned[^1] switch
        {
            // ㄵ ㄶ ㅄ ㅆ ㅈ ㅊ ㅌ ㅎ: 앉소, 않소, 없소, 있소, 맞소, 찾소, 같소, 그렇소. Not 장소, 미소, 연소, nor the noun 젖소.
            '소' => FinalConsonant(before) is 5 or 6 or 18 or 20 or 22 or 23 or 25 or 27 && !word.EndsWith("젖소", StringComparison.Ordinal),
            '오' => before is '하' or '이' or '되' or '마' or '주',
            _ => false,
        };
    }

    // Nouns in 외 that 다 follows as 하오체's ~외다 does: 의외, 예외, 제외, 열외, 야외, 교외, 해외, 실외, 국외, 과외, 내외.
    private static readonly string[] OeNouns = { "의외", "예외", "제외", "열외", "야외", "교외", "해외", "실외", "국외", "과외", "내외" };

    /// <summary>
    /// 하게체: "~네" (하네, 있네, 다네), "~게" after an open syllable (하게, 주게, 보게; not the nouns 가게, 무게, 지게),
    /// "~ㄹ세" and "~하세" (일세, 걸세), "~는가", "~인가", "~던가", "~은가", "~런가", "~나" after ㅆ or ㅄ (있나, 겠나,
    /// 했나), "~게나" and "~구먼". A lone "네" is the polite "yes", and is not one.
    /// </summary>
    private static bool IsHage(string cleaned)
    {
        var word = LastWord(cleaned);
        if (word.Length < 2)
        {
            return false;
        }

        if (word.EndsWith("게나", StringComparison.Ordinal) || word.EndsWith("구먼", StringComparison.Ordinal)
            || word.EndsWith("는가", StringComparison.Ordinal) || word.EndsWith("인가", StringComparison.Ordinal)
            || word.EndsWith("던가", StringComparison.Ordinal) || word.EndsWith("은가", StringComparison.Ordinal)
            || word.EndsWith("런가", StringComparison.Ordinal) || word.EndsWith("텐가", StringComparison.Ordinal)
            || word.EndsWith("겐가", StringComparison.Ordinal) || word.EndsWith("떤가", StringComparison.Ordinal)
            || word.EndsWith("닌가", StringComparison.Ordinal))
        {
            return true;
        }

        var before = word[^2];
        return word[^1] switch
        {
            '네' => true,
            '게' => FinalConsonant(before) == 0 && word is not ("가게" or "무게" or "지게"),
            '세' => FinalConsonant(before) == 8 || before is '하' or '보',
            '나' => FinalConsonant(before) is 18 or 20,
            _ => false,
        };
    }

    // 반말 a 하오체 speaker does not use: ~야, ~어/~아, ~해, ~잖아, ~냐, ~니, ~자, ~줘, ~봐, ~돼, ~래, ~거든, ~라고.
    // ~군, ~지, ~네, ~는데, ~니까 are not among them: the Korean patch's 하오체 speakers mix those in (Teldryn's
    // "기꺼이 도와드리지", "오랜만이군").
    private static readonly System.Text.RegularExpressions.Regex PlainEnding = new(@"(?:야|어|아|해|잖아|냐|니|자|줘|봐|돼|래|거든|라고)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex PlainPronoun = new(@"(?<![가-힣])(?:너(?:는|를|도|한테|에게|랑|가|의|만)?|네가|니가|널)(?![가-힣])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex HagePronoun = new(@"자네", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // ~거든 ends a sentence only before . ! ? … or the line's end; before a comma it is "if" ("그를 만나거든, 안부를 전해
    // 주구려"). No ending list reads it (a book quoting speech ends in it too), so it is taken here by itself.
    private static readonly System.Text.RegularExpressions.Regex ClosingGeodeun = new(@"거든\s*(?:[.!?…~]|$)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The level a line shows against a decided 하오체, 하게체 or 반말 that only another of the three uses; null when it
    /// fits or shows none. 존댓말 against the rest is the side check's; these three are one side, so Teldryn in Extended
    /// Hireling Dialogue, decided 하오체 to the player, kept lines in 반말 ("정신 바짝 차려야 해") and 하게체 ("자네")
    /// that no check named. A line with a 하오체 sentence fits 하오체 whatever else it has.
    /// </summary>
    internal static string? StrayFromDeclared(string? text, string declared)
    {
        if (declared is not ("하오체" or "하게체" or "반말" or "합쇼체"))
        {
            return null;
        }

        var (spoken, pieces) = OwnPieces(text);
        var hao = pieces.Any(piece => piece.Register == Register.Hao);
        var hage = HagePronoun.IsMatch(spoken) || pieces.Any(piece => piece.Register == Register.Hage && IsAddressedHage(piece.Part));
        var plain = IsPlain(spoken, pieces);
        // 합쇼체 against 해요체: Vaegra, decided 합쇼체 to Alara, said "…사람일 뿐이에요. …못 써요." A 해요체 piece that is
        // also a 하오체 noun with 요 ("열쇠요") does not count, nor ~지요 and ~군요, which formal speech uses too ("아시겠지요",
        // "모르겠군요" among Velteth's 합쇼체).
        var polite = pieces.Where(piece => piece.Register == Register.Polite && !IsWeakPolite(piece.Part)
                                           && !piece.Part.EndsWith("지요", StringComparison.Ordinal) && !piece.Part.EndsWith("군요", StringComparison.Ordinal))
            .Select(piece => LqaToneClassifier.Classify(piece.Part)).ToList();
        return declared switch
        {
            "하오체" when !hao && hage => "하게체",
            "하오체" when !hao && plain => "반말",
            "하게체" when hao && !hage => "하오체",
            "반말" when hao => "하오체",
            "반말" when HagePronoun.IsMatch(spoken) => "하게체",
            "합쇼체" when polite.Contains(ToneKind.Haeyo) && !polite.Contains(ToneKind.Hamnida) => "해요체",
            _ => null,
        };
    }

    /// <summary>
    /// Whether the line says something only 반말 says (~야, ~잖아, ~거든, 너), as <see cref="StrayFromDeclared"/> finds 반말
    /// against a decided 하오체; a 하오체 speaker's ~지, ~군, ~다 and ~네 are not. 하게체 does not hide it ("자네도 알잖아").
    /// </summary>
    internal static bool IsPlainCasual(string? text)
    {
        var (spoken, pieces) = OwnPieces(text);
        return IsPlain(spoken, pieces);
    }

    private static bool IsPlain(string spoken, List<(string Part, Register Register)> pieces)
        => PlainPronoun.IsMatch(spoken) || ClosingGeodeun.IsMatch(spoken)
           || pieces.Any(piece => piece.Register == Register.Casual && PlainEnding.IsMatch(piece.Part));

    /// <summary>The line's own sentences (stage directions, quotations and repeated cries left out), each with its level.</summary>
    private static (string Spoken, List<(string Part, Register Register)> Pieces) OwnPieces(string? text)
    {
        var spoken = Quoted.Replace(StageDirection.Replace(text ?? "", " "), " ");
        var parts = spoken.Split(new[] { '.', '!', '?', '…', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cries = Cries(parts);
        return (spoken, parts.Where(part => !cries.Contains(part)).Select(part => (Clean(part), SentenceLevel(part, keepShort: false))).ToList());
    }

    /// <summary>
    /// A 하게체 ending only a 하게체 speaker uses to the one spoken to. ~네 and ~나? are 반말's too, and ~가? asks oneself
    /// ("내가 이곳이 싫다고 말했던가?", "또 노드 유적인가"), as the Korean patch's 하오체 Teldryn does ("또 파괴를
    /// 불러내러 오셨나?"); a lone 그게 or 이게 is a pronoun ("그게, 전에 그를 한번 만난 적이 있다오").
    /// </summary>
    private static bool IsAddressedHage(string cleaned)
    {
        var word = LastWord(cleaned);
        if (word == cleaned && word.Length <= 2)
        {
            return false;
        }

        return word[^1] switch
        {
            '네' => false,
            '나' => word.EndsWith("게나", StringComparison.Ordinal),
            '가' => word.EndsWith("텐가", StringComparison.Ordinal) || word.EndsWith("겐가", StringComparison.Ordinal),
            _ => true,
        };
    }

    private static string LastWord(string cleaned)
    {
        var space = cleaned.LastIndexOfAny(new[] { ' ', '\t', '\n' });
        return cleaned[(space + 1)..];
    }

    // The final consonant's index (0 none, 8 ㄹ, 18 ㅄ, 20 ㅆ, 27 ㅎ ...), or -1 for a character that is not a syllable.
    private static int FinalConsonant(char c) => KoreanSyllables.IsHangulSyllable(c) ? (c - '가') % 28 : -1;

    // "야를님, 이걸 어떻게 말씀드려야 할지...", "어떻게 될지", "왜 그런지": this 지 asks "whether" in a clause left open,
    // and is not 반말's 지. A polite line that trails off so was counted as 반말 among Serana's lines to the Jarl.
    private static readonly string[] OpenClauseEndings =
    {
        "할지", "될지", "을지", "는지", "은지", "인지", "던지", "런지", "올지", "갈지", "볼지", "낼지", "릴지",
        // "황량한 땅의 구석구석까지, 그대가 ... 전해주기를.": the particle 까지, not 반말's 지.
        "까지",
    };

    private static readonly string[] CasualClauseEndings = { "텐데", "테니", "테니까", "는데", "은데", "던데", "건데", "니까", "안 가", "못 가" };

    private static bool EndsInCasualClause(string? text)
    {
        var cleaned = Clean(text);
        var comma = cleaned.LastIndexOf(',');
        if (comma > 0 && !cleaned[(comma + 1)..].Trim().Contains(' '))
        {
            cleaned = cleaned[..comma].TrimEnd(); // a closing vocative: "…텐데, 챔피언"
        }

        if (!cleaned.Contains(' ') || !CasualClauseEndings.Any(e => cleaned.EndsWith(e, StringComparison.Ordinal)))
        {
            return false;
        }

        // ~ㅂ니까 and ~습니까 ask in 합니다체.
        return !(cleaned.EndsWith("니까", StringComparison.Ordinal) && cleaned.Length >= 3
                 && (FinalConsonant(cleaned[^3]) == 17 || cleaned[^3] == '습'));
    }

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

    private static Register SpokenLevel(string? text, bool keepShort)
    {
        var cleaned = LqaScanner.StripUiTokens(text ?? "").Trim().TrimEnd(' ', '.', ',', '!', '?', '…', '"', '\'', '”', '’', ')', '~', '-');
        // A closing vocative is no ending: "무슨 일로 오셨습니까, 친구여" is judged by "오셨습니까".
        var comma = cleaned.LastIndexOf(',');
        if (comma > 0 && !cleaned[(comma + 1)..].Trim().Contains(' '))
        {
            var clause = cleaned[..comma];
            var vocativeRegister = Ending(clause, out var plainDa);
            if (vocativeRegister != Register.Unknown && !plainDa)
            {
                return vocativeRegister;
            }

            cleaned = clause.TrimEnd();
        }

        if (cleaned.Length == 0 || !cleaned.Contains(' ') && (Interjection.IsMatch(cleaned) || !keepShort && cleaned.Length <= 2))
        {
            return Register.Unknown; // "Mercy!" as "자비를!" or a lone name says nothing about the level.
        }

        if (cleaned.EndsWith("죠", StringComparison.Ordinal))
        {
            return Register.Polite;
        }

        return CasualEndings.Any(e => cleaned.EndsWith(e, StringComparison.Ordinal)) || IsCasualVerbEnding(LastWord(cleaned))
            ? Register.Casual
            : Register.Unknown;
    }

    // 반말's let's (가자, 하자), orders and plain endings in 라 (해라, 가라, 몰라, 바라), questions in ㄹ까 (볼까, 할까) and
    // 건가: thirteen of Laracin's 반말 lines in Meridia's Order had no level, so the review found them by hand. Only the
    // forms the Korean patch's dialogue ends verbs with; not the nouns 여자, 신자, 지도자 or 나라, nor names (알라라, 세라).
    private static readonly HashSet<string> CasualVerbEndings = new(StringComparer.Ordinal)
    {
        "가자", "보자", "하자", "나자", "내자", "주자", "치자", "두자", "살자", "말자", "우자", "붙자", "짓자", "있자", "놓자", "오자",
        "어라", "아라", "여라", "해라", "하라", "가라", "마라", "거라", "려라", "봐라", "와라", "켜라", "서라", "워라", "춰라", "내라",
        "쳐라", "러라", "줘라", "져라", "벼라", "둬라", "으라", "몰라", "달라", "말라", "바라", "보라", "오라", "더라", "노라", "니라",
        "리라", "건가", "나가",
    };

    private static bool IsCasualVerbEnding(string word)
        => word.Length >= 2
           && (CasualVerbEndings.Contains(word[^2..])
               || word is "버리자" or "내리자"
               || word[^1] == '까' && FinalConsonant(word[^2]) == 8);
}
