using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static bool LooksLikeKoreanText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var ch in text)
        {
            if (ch >= '가' && ch <= '힣')
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildLossAndRecoveryPhraseKo(string attrEn)
    {
        if (string.Equals(attrEn, "Health", StringComparison.OrdinalIgnoreCase))
        {
            return "체력 감소 및 회복";
        }

        var attrKo = TranslateKnownSubjectKo(attrEn);
        return $"{attrKo} 소모 및 회복";
    }

    private static string BuildFortifiesSkillsAndAttrKo(Match match)
    {
        var skillsRaw = match.Groups["skills"].Value.Trim();
        var magHasPoints = match.Groups["magPoints"].Success;
        var attr = match.Groups["attr"].Value.Trim();
        var attrMag = match.Groups["attrMag"].Value.Trim();
        var magToken = match.Groups["mag"].Value.Trim();
        var durToken = match.Groups["dur"].Value.Trim();

        var skills = SplitEnglishList(skillsRaw);
        for (var i = 0; i < skills.Count; i++)
        {
            skills[i] = TranslateKnownSubjectKo(skills[i]);
        }

        var skillsKo = skills.Count switch
        {
            0 => TranslateKnownSubjectKo(skillsRaw),
            1 => skills[0],
            _ => string.Join(" 및 ", skills),
        };

        var attrKo = TranslateKnownSubjectKo(attr);
        var magUnit = magHasPoints ? "포인트" : "만큼";
        // Attribute magnitudes are typically expressed in points.
        var attrUnit = "포인트";

        return $"{durToken}초 동안 {skillsKo} 기술이 {magToken}{magUnit}, {attrKo}{GetSubjectParticle(attrKo)} {attrMag}{attrUnit} 증가합니다.";
    }

    private static List<string> SplitEnglishList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        // Normalize whitespace to make splitting predictable.
        var normalized = Regex.Replace(text.Trim(), @"\s+", " ", RegexOptions.CultureInvariant);
        var parts = Regex.Split(normalized, @"\s*(?:,|\b(?:and|och)\b)\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var list = new List<string>(capacity: parts.Length);
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
                list.Add(s);
            }
        }
        return list;
    }

    private static string TranslateKnownSubjectKo(string subject)
    {
        var s = subject.Trim();
        return KnownSubjectsKo.TryGetValue(s, out var ko) ? ko : s;
    }

    private static string GetSubjectParticle(string noun)
    {
        if (string.IsNullOrWhiteSpace(noun))
        {
            return "이";
        }

        var s = noun.TrimEnd();
        var ch = s[^1];
        if (ch < '가' || ch > '힣')
        {
            return "이";
        }

        // Hangul syllable decomposition: (ch - 0xAC00) % 28 == 0 => no final consonant.
        var hasFinal = ((ch - '가') % 28) != 0;
        return hasFinal ? "이" : "가";
    }

    private static string GetObjectParticle(string noun)
    {
        if (string.IsNullOrWhiteSpace(noun))
        {
            return "을";
        }

        var s = noun.TrimEnd();
        var ch = s[^1];
        if (ch < '가' || ch > '힣')
        {
            return "을";
        }

        var hasFinal = ((ch - '가') % 28) != 0;
        return hasFinal ? "을" : "를";
    }

    private static bool LooksLikeBadMagDurUsage(string dest)
    {
        // A few strong signals that the placeholders got swapped or placed on the wrong particle.
        // We keep this conservative: only auto-fix when it is very likely wrong.
        var durLooksAmount = Regex.IsMatch(dest, @"[+-]?<\s*dur\s*>\s*(%|퍼센트|만큼|점|포인트|수치)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var magLooksTime = Regex.IsMatch(
            dest,
            @"([+-]?<\s*mag\s*>\s*%?\s*(초간|초|분|시간|일|주|개월|년|동안|간))|((초간|초|분|시간|일|주|개월|년|동안|간)\s*[+-]?<\s*mag\s*>\s*%?)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );
        var magHasPercent = Regex.IsMatch(dest, @"[+-]?<\s*mag\s*>\s*%", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var durHasPercent = Regex.IsMatch(dest, @"[+-]?<\s*dur\s*>\s*%", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        return durLooksAmount || magLooksTime || (!magHasPercent && durHasPercent);
    }

    private static string NormalizeDuplicateSigns(string dest)
    {
        if (string.IsNullOrWhiteSpace(dest))
        {
            return dest;
        }

        // LLMs sometimes add an extra + / - before a signed placeholder that already includes a sign (e.g., "++<mag>").
        // Normalize these to keep game placeholders valid.
        var working = DuplicateSignedPlaceholderRegex.Replace(
            dest,
            m =>
            {
                if (m.Groups["plus"].Success && m.Groups["plus2"].Success)
                {
                    return "+<mag>";
                }
                if (m.Groups["minus"].Success && m.Groups["minus2"].Success)
                {
                    return "-<mag>";
                }
                return m.Value;
            }
        );

        // Also normalize the common "++<mag>" / "--<mag>" without spaces.
        working = Regex.Replace(working, @"\+\+<\s*mag\s*>", "+<mag>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        working = Regex.Replace(working, @"--<\s*mag\s*>", "-<mag>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return working;
    }

    private static bool LooksLikeSwappedInKorean(string dest, out string magToken, out string durToken)
    {
        magToken = "";
        durToken = "";

        var mag = MagRegex.Matches(dest);
        var dur = DurRegex.Matches(dest);
        if (mag.Count != 1 || dur.Count != 1)
        {
            return false;
        }

        magToken = mag[0].Value;
        durToken = dur[0].Value;
        return LooksLikeBadMagDurUsage(dest);
    }

    private static string SwapOnce(string text, string a, string b)
    {
        const string tmp = "__XT_SWAP_TMP__";
        var working = text.Replace(a, tmp, StringComparison.OrdinalIgnoreCase);
        working = working.Replace(b, a, StringComparison.OrdinalIgnoreCase);
        working = working.Replace(tmp, b, StringComparison.OrdinalIgnoreCase);
        return working;
    }

    private static bool IsKorean(string lang)
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

        // Common culture-style codes: ko-KR / ko_kr
        if (s.StartsWith("ko-", StringComparison.OrdinalIgnoreCase) || s.StartsWith("ko_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Some tools store display names or localized names.
        if (s.IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.Equals(s, "한국어", StringComparison.OrdinalIgnoreCase)
               || s.IndexOf("한국", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
