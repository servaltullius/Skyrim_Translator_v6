using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Very narrow, deterministic fixes for common semantic role inversions in Korean outputs.
/// Intended for short quest/UI sentences where models sometimes invert "protect X from Y" patterns.
/// </summary>
internal static class KoreanProtectFromFixer
{
    private static readonly Regex SourceProtectFromAttackRegex = new(
        pattern: @"\bprotect(?:ing|ed|s)?\b.+?\bfrom\b.+?\battack\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private const string IncomingKoPattern =
        @"(?:다가오는|밀려오는|습격해오는|습격해\s+오는|공격해오는|공격해\s+오는|쳐들어오는|몰려오는|들이닥치는)";

    // Keep this list conservative: only high-confidence attacker nouns.
    private static readonly string[] LikelyAttackersKo =
    {
        "산적", "도적", "강도", "무법자",
        "드래곤", "흡혈귀", "광신도", "거인", "늑대인간",
    };

    private static readonly Regex DestProtectFromAttackRegex = new(
        pattern: @"(?<incoming>" + IncomingKoPattern + @"\s+)?"
                 + @"(?<attacker>[\p{L}\p{N} \-'\u2019]{1,60}?)의\s*"
                 + @"(?<attackNoun>공격|습격|공세|기습)\s*(?<from>(?:으)?로부터)\s*"
                 + @"(?<protected>[\p{L}\p{N} \-'\u2019]{1,60}?)(?<objParticle>을|를)\s*보호",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex DestProtectFromAttackIncomingAfterFromRegex = new(
        pattern: @"(?<attacker>[\p{L}\p{N} \-'\u2019]{1,60}?)의\s*"
                 + @"(?<attackNoun>공격|습격|공세|기습)\s*(?<from>(?:으)?로부터)\s*"
                 + @"(?<incoming>" + IncomingKoPattern + @")\s+"
                 + @"(?<protected>[\p{L}\p{N} \-'\u2019]{1,60}?)(?<objParticle>을|를)\s*보호",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex DestProtectFromDirectFromRegex = new(
        pattern: @"(?<incoming>" + IncomingKoPattern + @"\s+)?"
                 + @"(?<attacker>[\p{L}\p{N} \-'\u2019]{1,60}?)(?<attackerPlural>들)?\s*(?:으)?로부터\s*"
                 + @"(?<protected>[\p{L}\p{N} \-'\u2019]{1,60}?)(?<objParticle>을|를)\s*보호",
        options: RegexOptions.CultureInvariant
    );

    private static readonly string[] AttackNounsKo = { "공격", "습격", "공세", "기습" };

    internal static string Fix(string targetLang, string sourceText, string destText)
    {
        if (!IsKoreanLanguage(targetLang))
        {
            return destText;
        }

        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(destText))
        {
            return destText;
        }

        // Avoid touching intermediate tokenized forms.
        if (destText.IndexOf("__XT_", StringComparison.Ordinal) >= 0)
        {
            return destText;
        }

        // Only attempt when the English explicitly matches "protect ... from ... attack" patterns.
        if (!SourceProtectFromAttackRegex.IsMatch(sourceText))
        {
            return destText;
        }

        // Some model outputs misplace the "incoming" phrase after "으로부터":
        //   "산적의 공격으로부터 습격해 오는 템테이션 하우스를 보호..."
        // Move it back to modify the attack phrase:
        //   "습격해 오는 산적의 공격으로부터 템테이션 하우스를 보호..."
        var normalized = DestProtectFromAttackIncomingAfterFromRegex.Replace(
            destText,
            m =>
            {
                var incoming = m.Groups["incoming"].Value.Trim();
                var attacker = m.Groups["attacker"].Value.Trim();
                var attackNoun = m.Groups["attackNoun"].Value;
                var from = m.Groups["from"].Value;
                var protectedNoun = m.Groups["protected"].Value.Trim();

                var objParticle = ChooseObjectParticle(protectedNoun);
                return incoming + " " + attacker + "의 " + attackNoun + from + " " + protectedNoun + objParticle + " 보호";
            }
        );

        var m = DestProtectFromAttackRegex.Match(normalized);
        if (!m.Success)
        {
            // Some model outputs omit the explicit "의 공격" noun phrase and produce:
            //   "<incoming> <protected>로부터 <attacker>을 보호..."
            // e.g., "습격해오는 템테이션 하우스들로부터 산적을 보호..."
            // We can still fix role inversion deterministically by swapping attacker/protected and restoring "의 공격".
            var dm = DestProtectFromDirectFromRegex.Match(normalized);
            if (!dm.Success)
            {
                return normalized;
            }

            var incoming2 = dm.Groups["incoming"].Value;
            var attacker2 = (dm.Groups["attacker"].Value + dm.Groups["attackerPlural"].Value).Trim();
            var protected2 = dm.Groups["protected"].Value.Trim();

            if (!ContainsAny(protected2, LikelyAttackersKo))
            {
                return normalized;
            }
            if (ContainsAny(attacker2, LikelyAttackersKo))
            {
                return normalized;
            }

            // Avoid mangling already-explicit attack noun phrases that just didn't match the main regex.
            if (ContainsAny(attacker2, AttackNounsKo))
            {
                return normalized;
            }

            var newAttacker2 = protected2;
            var newProtected2 = attacker2;
            var objParticle2 = ChooseObjectParticle(newProtected2);

            var replacement2 = incoming2 + newAttacker2 + "의 공격으로부터 " + newProtected2 + objParticle2 + " 보호";
            return normalized.Substring(0, dm.Index) + replacement2 + normalized.Substring(dm.Index + dm.Length);
        }

        var incoming = m.Groups["incoming"].Value;
        var attacker = m.Groups["attacker"].Value.Trim();
        var attackNoun = m.Groups["attackNoun"].Value;
        var from = m.Groups["from"].Value;
        var protectedNoun = m.Groups["protected"].Value.Trim();

        // Swap only when the protected noun phrase strongly looks like an attacker group (e.g., "산적"),
        // and the attacker side does NOT already look like an attacker group.
        if (!ContainsAny(protectedNoun, LikelyAttackersKo))
        {
            return normalized;
        }
        if (ContainsAny(attacker, LikelyAttackersKo))
        {
            return normalized;
        }

        var newAttacker = protectedNoun;
        var newProtected = attacker;
        var objParticle = ChooseObjectParticle(newProtected);

        var replacement = incoming + newAttacker + "의 " + attackNoun + from + " " + newProtected + objParticle + " 보호";
        return normalized.Substring(0, m.Index) + replacement + normalized.Substring(m.Index + m.Length);
    }

    private static bool ContainsAny(string text, IReadOnlyList<string> needles)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var n in needles)
        {
            if (!string.IsNullOrWhiteSpace(n) && text.Contains(n, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ChooseObjectParticle(string nounPhrase)
    {
        if (string.IsNullOrWhiteSpace(nounPhrase))
        {
            return "를";
        }

        var last = FindLastRelevantChar(nounPhrase);
        if (last == '\0')
        {
            return "를";
        }

        if (IsHangulSyllable(last))
        {
            return HasFinalConsonant(last) ? "을" : "를";
        }

        if (char.IsDigit(last))
        {
            return DigitHasFinalConsonant(last) ? "을" : "를";
        }

        if ((last >= 'A' && last <= 'Z') || (last >= 'a' && last <= 'z'))
        {
            var lower = char.ToLowerInvariant(last);
            return IsLatinVowel(lower) ? "를" : "을";
        }

        return "를";
    }

    private static char FindLastRelevantChar(string text)
    {
        for (var i = text.Length - 1; i >= 0; i--)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (IsHangulSyllable(c) || char.IsDigit(c) || ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
            {
                return c;
            }
        }

        return '\0';
    }

    private static bool IsHangulSyllable(char c) => c >= 0xAC00 && c <= 0xD7A3;

    private static bool HasFinalConsonant(char hangulSyllable)
    {
        var index = hangulSyllable - 0xAC00;
        var jong = index % 28;
        return jong != 0;
    }

    private static bool IsLatinVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static bool DigitHasFinalConsonant(char digit)
    {
        // Korean digit readings:
        // 0=영(ㅇ), 1=일(ㄹ), 2=이(없음), 3=삼(ㅁ), 4=사(없음),
        // 5=오(없음), 6=육(ㄱ), 7=칠(ㄹ), 8=팔(ㄹ), 9=구(없음)
        return digit switch
        {
            '0' => true,
            '1' => true,
            '2' => false,
            '3' => true,
            '4' => false,
            '5' => false,
            '6' => true,
            '7' => true,
            '8' => true,
            '9' => false,
            _ => true,
        };
    }

    private static bool IsKoreanLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim();
        if (string.Equals(s, "korean", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ko", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.StartsWith("ko-", StringComparison.OrdinalIgnoreCase) || s.StartsWith("ko_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.Equals(s, "한국어", StringComparison.OrdinalIgnoreCase)
               || s.IndexOf("한국", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
