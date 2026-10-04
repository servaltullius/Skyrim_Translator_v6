using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Narrow Korean post-edits that need the source to be safe. Each one fixes a pattern reviewers corrected again
/// and again (Elden Rim, Serana): "Fortify X" names written "강화 X", "Hey Nemiko!" without the comma Korean
/// needs after the greeting, and Hanja glosses ("범인(凡人)") nobody asked for.
/// </summary>
internal static class KoreanSourceAwareFixes
{
    // A whole short name: "Fortify Secret Book", "Fortify Mystic 3". Not a sentence ("Fortify Health by 10 points.").
    private static readonly Regex FortifyNameSourceRegex = new(
        @"^Fortify(?:\s+[A-Z][\w'’-]*){1,6}(?:\s+(?:\d+|[IVX]+))?$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex FortifyFirstDestRegex = new(
        @"^강화\s+(?<rest>[^\r\n.!?]+?)(?<num>\s+(?:\d+|[IVX]+))?$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex GreetingSourceRegex = new(
        @"^(?:Hey|Hi|Hello)\s+[A-Z][\w'’-]*\s*[!.?]+$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex GreetingDestRegex = new(
        @"^(?<greeting>안녕|이봐|어이|저기|여기|야)\s+(?<name>[가-힣]+)(?<end>\s*[!.?]+)$",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex HanjaGlossRegex = new(
        @"(?<=[가-힣])\((?:[㐀-䶿一-鿿豈-﫿]+)\)",
        RegexOptions.CultureInvariant
    );

    private static readonly Regex IdeographRegex = new(@"[㐀-䶿一-鿿豈-﫿]", RegexOptions.CultureInvariant);

    internal static string Apply(string targetLang, string sourceText, string text)
    {
        if (!LanguageHelper.IsKoreanLanguage(targetLang) || string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(sourceText))
        {
            return text;
        }

        var source = sourceText.Trim();
        var working = text;
        working = PutFortifyLast(source, working);
        working = AddCommaAfterGreeting(source, working);
        working = RemoveHanjaGloss(source, working);
        return working;
    }

    /// <summary>"Fortify Counter" → "반격 강화", as the official effect names (체력 강화) and the reviews write it.</summary>
    private static string PutFortifyLast(string source, string text)
    {
        if (!FortifyNameSourceRegex.IsMatch(source))
        {
            return text;
        }

        var m = FortifyFirstDestRegex.Match(text.Trim());
        if (!m.Success)
        {
            return text;
        }

        var rest = m.Groups["rest"].Value.Trim();
        return rest.EndsWith("강화", StringComparison.Ordinal) ? text : $"{rest} 강화{m.Groups["num"].Value}";
    }

    private static string AddCommaAfterGreeting(string source, string text)
    {
        if (!GreetingSourceRegex.IsMatch(source))
        {
            return text;
        }

        var m = GreetingDestRegex.Match(text.Trim());
        return m.Success ? $"{m.Groups["greeting"].Value}, {m.Groups["name"].Value}{m.Groups["end"].Value}" : text;
    }

    /// <summary>
    /// "반기(半旗)" → "반기": a Hanja gloss right after a Hangul word. Kept when the source itself has
    /// ideographs (a Chinese source may name the term on purpose).
    /// </summary>
    private static string RemoveHanjaGloss(string source, string text)
    {
        if (IdeographRegex.IsMatch(source) || !HanjaGlossRegex.IsMatch(text))
        {
            return text;
        }

        return HanjaGlossRegex.Replace(text, "");
    }
}
