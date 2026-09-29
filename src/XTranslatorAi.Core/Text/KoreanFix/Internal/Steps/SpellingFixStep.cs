using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class SpellingFixStep : IKoreanFixStep
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    // 되었 → 됐 (가장 흔한 AI 맞춤법 오류)
    private static readonly Regex DoeEotRegex = new(
        pattern: @"되었",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    // 되어 → 돼 (축약형)
    private static readonly Regex DoeEoRegex = new(
        pattern: @"되어",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    // 됬 → 됐 (잘못된 축약)
    private static readonly Regex DwaetWrongRegex = new(
        pattern: @"됬",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    // 몇일 → 며칠 (표준어 규정)
    private static readonly Regex MyeotIlRegex = new(
        pattern: @"몇일",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    // 않되 → 안 돼 / 안되 → 안 돼 (common AI error: 되/돼 after 안)
    // 안 + 되 + 어미 없음 → 안 돼 (standalone "안되" at word boundary)
    private static readonly Regex AnDoeRegex = new(
        pattern: @"않되(?![었어])",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    public string Apply(KoreanFixContext context, string text)
    {
        var working = text;

        // 됬 → 됐 (잘못된 축약)
        if (working.IndexOf("됬", StringComparison.Ordinal) >= 0)
        {
            working = DwaetWrongRegex.Replace(working, "됐");
        }

        // 몇일 → 며칠
        if (working.IndexOf("몇일", StringComparison.Ordinal) >= 0)
        {
            working = MyeotIlRegex.Replace(working, "며칠");
        }

        // 않되 → 않돼 (안 되다의 준말)
        if (working.IndexOf("않되", StringComparison.Ordinal) >= 0)
        {
            working = AnDoeRegex.Replace(working, "않돼");
        }

        // 되었 → 됐
        if (working.IndexOf("되었", StringComparison.Ordinal) >= 0)
        {
            working = DoeEotRegex.Replace(working, "됐");
        }

        // 되어 → 돼
        if (working.IndexOf("되어", StringComparison.Ordinal) >= 0)
        {
            working = DoeEoRegex.Replace(working, "돼");
        }

        return working;
    }
}
