using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// Finds a glossary term written as its English sound instead of the glossary translation. Serana Dialogue
/// Add-On said 뱀파이어로 변한 for "since I was turned": the source never says "vampire", so the glossary
/// (Vampire → 흡혈귀) was not applied and the model used the loanword. Words are compared by sound, as consonant
/// classes with the places of vowels between them: vampire and 뱀파이어 are both B·V·M·B·V.
/// Only terms the glossary translates (흡혈귀, 환영마법) are checked, not names it spells out (사요니), and a
/// Korean word that spells a word of the source line (세피라 for Sefirah) is that word.
/// </summary>
internal static class GlossaryLoanwordRule
{
    internal sealed record Term(GlossaryEntry Entry, string FirstVowels);

    private const char Vowel = 'V';

    private static readonly Regex EnglishTermRegex = new(@"^[A-Za-z]+$", RegexOptions.CultureInvariant);
    private static readonly Regex EnglishWordRegex = new(@"[A-Za-z]+", RegexOptions.CultureInvariant);
    private static readonly Regex HangulWordRegex = new(@"[가-힣]+", RegexOptions.CultureInvariant);

    // Particles stripped (twice at most: 수업이라도 → 수업이라 → 수업) to reach the word itself.
    private static readonly string[] Suffixes =
    {
        "에게서", "한테서", "에서는", "이라는", "께서", "에게", "한테", "에서", "에선", "에는", "이랑", "이라", "이란", "라는", "처럼", "보다", "까지", "부터", "으로",
        "이나", "은", "는", "이", "가", "을", "를", "의", "와", "과", "도", "만", "에", "엔", "로", "랑", "나",
    };

    // ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ; ㅇ at the start of a syllable is silent.
    private static readonly char[] Initials = { 'K', 'K', 'N', 'T', 'T', 'L', 'M', 'B', 'B', 'S', 'S', '\0', 'J', 'J', 'J', 'K', 'T', 'B', 'H' };

    // Final consonants as they sound; a final ㅇ is the "ng" of English.
    private static readonly char[] Finals =
    {
        '\0', 'K', 'K', 'K', 'N', 'N', 'N', 'T', 'L', 'K', 'M', 'L', 'L', 'L', 'B', 'L', 'M', 'B', 'B', 'T', 'T', 'G', 'T', 'T', 'K', 'T', 'B', '\0',
    };

    private const string Vowels = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";

    // The first vowel of a borrowed word follows the English spelling: vampire → 뱀, cuirass → 퀴 or 큐 (카리우스 is Carius).
    private static readonly Dictionary<char, string> FirstVowelsByLetter = new()
    {
        ['a'] = "ㅏㅐㅓㅔㅑㅒㅗㅘㅙㅝ",
        ['e'] = "ㅔㅐㅣㅓㅖㅢㅞㅟ",
        ['i'] = "ㅣㅏㅓㅟㅢ",
        ['o'] = "ㅗㅓㅏㅘㅚㅜㅛㅝ",
        ['u'] = "ㅜㅓㅠㅝㅟㅚ",
        ['y'] = "ㅣㅏㅐㅠㅟㅢㅑ",
    };

    private const int VowelEu = 18; // ㅡ, the vowel Korean adds between consonants (클로크 for cloak)
    private const int FinalNg = 21;
    private const int SilentInitial = 11;

    public static IReadOnlyDictionary<string, Term> Build(IReadOnlyList<GlossaryEntry> glossary)
    {
        var index = new Dictionary<string, Term>(StringComparer.Ordinal);
        foreach (var entry in glossary)
        {
            var source = (entry.SourceTerm ?? "").Trim();
            if (!EnglishTermRegex.IsMatch(source))
            {
                continue;
            }

            var sound = EnglishSound(source);
            if (sound.Count(c => c != Vowel) < 3)
            {
                continue;
            }

            var target = new string((entry.TargetTerm ?? "").Where(IsHangulSyllable).ToArray());
            var targetSound = target.Length == 0 ? null : KoreanSound(target);
            if (targetSound == null || (double)Distance(targetSound, sound) / Math.Max(targetSound.Length, sound.Length) <= 0.5)
            {
                continue;
            }

            index.TryAdd(sound, new Term(entry, FirstVowelsOf(source)));
        }

        return index;
    }

    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        IReadOnlyDictionary<string, Term> index,
        List<LqaIssue> issues
    )
    {
        if (!isKorean || index.Count == 0 || string.IsNullOrEmpty(destText))
        {
            return;
        }

        HashSet<string>? sourceSounds = null;
        foreach (var word in HangulWordRegex.Matches(destText).Select(m => m.Value).Distinct(StringComparer.Ordinal))
        {
            var levels = StripLevels(word);
            var stem = levels[^1];
            if (stem.Length < 3 || IsWrittenLikeNativeWord(stem))
            {
                continue;
            }

            var sound = KoreanSound(stem);
            if (sound == null || !index.TryGetValue(sound, out var found))
            {
                continue;
            }

            var term = found.Entry;
            if (stem.Contains(term.TargetTerm.Replace(" ", ""), StringComparison.Ordinal) || !found.FirstVowels.Contains(FirstKoreanVowel(stem)))
            {
                continue;
            }

            sourceSounds ??= SourceSounds(sourceText);
            if (levels.Any(level => KoreanSound(level) is { } s && sourceSounds.Contains(s)))
            {
                continue;
            }

            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "glossary_variant",
                $"용어집과 다른 표기: '{stem}' → '{term.TargetTerm}' ({term.SourceTerm})", sourceText, destText));
            return;
        }
    }

    internal static string EnglishSound(string word)
    {
        var w = new string(word.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray());
        var sb = new StringBuilder();
        for (var i = 0; i < w.Length;)
        {
            var c = w[i];
            var next = i + 1 < w.Length ? w[i + 1] : '\0';
            switch (i + 1 < w.Length ? w.Substring(i, 2) : "")
            {
                case "ph": Append(sb, 'B'); i += 2; continue;
                case "th": Append(sb, 'T'); i += 2; continue;
                case "ch": Append(sb, 'J'); i += 2; continue;
                case "sh": Append(sb, 'S'); i += 2; continue;
                case "ck": Append(sb, 'K'); i += 2; continue;
                case "ng": Append(sb, 'G'); i += 2; continue;
                case "qu": Append(sb, 'K'); Append(sb, Vowel); i += 2; continue;
                case "gh": i += 2; continue;
            }

            if (IsVowelLetter(c))
            {
                var silentE = c == 'e' && i == w.Length - 1 && i > 0 && !IsVowelLetter(w[i - 1]);
                var glide = c == 'y' && IsVowelLetter(next);
                if (!silentE && !glide)
                {
                    Append(sb, Vowel);
                }
            }
            else
            {
                switch (c)
                {
                    case 'w':
                        break;
                    case 'h':
                        if ((i == 0 || IsVowelLetter(w[i - 1])) && IsVowelLetter(next))
                        {
                            Append(sb, 'H');
                        }

                        break;
                    // Korean drops an r that no vowel follows (탈모어 for Thalmor, 뱀파이어 for vampire).
                    case 'r':
                        if (IsVowelLetter(next) && !(next == 'e' && i + 2 == w.Length))
                        {
                            Append(sb, 'L');
                        }

                        break;
                    case 'c':
                        Append(sb, next is 'e' or 'i' or 'y' ? 'S' : 'K');
                        break;
                    case 'x':
                        Append(sb, 'K');
                        Append(sb, 'S');
                        break;
                    default:
                        Append(sb, c switch
                        {
                            'b' or 'v' or 'p' or 'f' => 'B',
                            'k' or 'g' or 'q' => 'K',
                            'd' or 't' => 'T',
                            's' => 'S',
                            'z' or 'j' => 'J',
                            'l' => 'L',
                            'm' => 'M',
                            'n' => 'N',
                            _ => '\0',
                        });
                        break;
                }
            }

            i++;
        }

        return sb.ToString();
    }

    private static string FirstVowelsOf(string englishWord)
    {
        var w = englishWord.ToLowerInvariant();
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            var isConsonantY = c == 'y' && i + 1 < w.Length && IsVowelLetter(w[i + 1]);
            var isQu = c == 'u' && i > 0 && w[i - 1] == 'q';
            if (FirstVowelsByLetter.TryGetValue(c, out var vowels) && !isConsonantY && !isQu)
            {
                return vowels;
            }
        }

        return Vowels;
    }

    private static char FirstKoreanVowel(string word)
    {
        foreach (var ch in word)
        {
            var vowel = (ch - 0xAC00) % 588 / 28;
            if (vowel != VowelEu)
            {
                return Vowels[vowel];
            }
        }

        return Vowels[VowelEu];
    }

    internal static string? KoreanSound(string word)
    {
        var sb = new StringBuilder();
        foreach (var ch in word)
        {
            if (!IsHangulSyllable(ch))
            {
                return null;
            }

            var code = ch - 0xAC00;
            Append(sb, Initials[code / 588]);
            if (code % 588 / 28 != VowelEu)
            {
                Append(sb, Vowel);
            }

            Append(sb, Finals[code % 28]);
        }

        return sb.ToString();
    }

    // Verbs made with 하다, 되다 or 버리다 (부여될, 써버리는) are not borrowed nouns.
    private static readonly string[] VerbEndings = { "하", "한", "할", "함", "해", "했", "되", "된", "될", "됨", "돼", "됐", "버리" };

    // Korean writes a consonant before a vowel into the next syllable in borrowed words (일루전, not 일우전), and
    // does not repeat a vowel (그라아악). Both happen in native words and particles: 알아선, 수업이라.
    internal static bool IsWrittenLikeNativeWord(string stem)
    {
        if (VerbEndings.Any(ending => stem.EndsWith(ending, StringComparison.Ordinal)))
        {
            return true;
        }

        for (var i = 1; i < stem.Length; i++)
        {
            var previous = stem[i - 1] - 0xAC00;
            var current = stem[i] - 0xAC00;
            if (current / 588 != SilentInitial)
            {
                continue;
            }

            if (previous % 28 is not (0 or FinalNg) || previous % 588 / 28 == current % 588 / 28)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> StripLevels(string word)
    {
        var levels = new List<string> { word };
        for (var n = 0; n < 2; n++)
        {
            var current = levels[^1];
            var suffix = Suffixes.FirstOrDefault(s => current.Length > s.Length && current.EndsWith(s, StringComparison.Ordinal));
            if (suffix == null)
            {
                break;
            }

            levels.Add(current[..^suffix.Length]);
        }

        return levels;
    }

    // "the Ideal Masters" names 마스터: plural words are also matched without their s.
    private static HashSet<string> SourceSounds(string sourceText)
    {
        var sounds = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in EnglishWordRegex.Matches(sourceText ?? ""))
        {
            sounds.Add(EnglishSound(match.Value));
            if (match.Length > 3 && match.Value.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                sounds.Add(EnglishSound(match.Value[..^1]));
            }
        }

        return sounds;
    }

    private static void Append(StringBuilder sb, char sound)
    {
        if (sound != '\0' && (sb.Length == 0 || sb[^1] != sound))
        {
            sb.Append(sound);
        }
    }

    private static bool IsVowelLetter(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static bool IsHangulSyllable(char c) => c is >= '가' and <= '힣';

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
