using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class SpellingFixStep : IKoreanFixStep
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    // "되었/되어"는 맞춤법 오류가 아니라 고치지 않는다. 공식 번역은 "되었습니다"를 "됐습니다"보다 훨씬 많이 쓰고(42:3),
    // 이 후처리는 TM 적용 행에도 돌아 공식 문장을 축약형으로 바꿨다(2026-10-05 결정 4).

    // 됬 → 됐 (잘못된 축약)
    private static readonly Regex DwaetWrongRegex = new(
        pattern: @"됬",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    // 몇일 → 며칠 (표준어 규정)
    // Not before 까: 몇일까 is 몇 + 일까 ("how many would it be?").
    private static readonly Regex MyeotIlRegex = new(
        pattern: @"몇일(?!까)",
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

        return working;
    }
}
