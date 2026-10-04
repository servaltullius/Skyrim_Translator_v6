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

    // "않되"는 고치지 않는다. 대부분 맞는 연결 어미 "-지 않되"("해치지 않되, 놓아주지도 마라")이고,
    // 예전 규칙이 만들던 "않돼"는 어떤 경우에도 맞춤법이 아니다(틀린 "않되"의 바른 형태는 "안 돼").
    // 띄어쓰기까지 판단해야 하는 교정이라 결정적 후처리에서는 손대지 않는다.

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
