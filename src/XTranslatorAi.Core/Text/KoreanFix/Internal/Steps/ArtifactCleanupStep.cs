using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class ArtifactCleanupStep : IKoreanFixStep
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex HaGiBeforeStateNounRegex = new(
        pattern: @"(?<stem>[가-힣]{2,30})하기(?=\s*상태)",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex HaGiBeforeHapNidaRegex = new(
        pattern: @"(?<stem>[가-힣]{2,30})하기\s*합니다",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex WeaponGoodsDropArtifactRegex = new(
        pattern: @"무기\s*(?:을|를)?\s*물건\s*전달(?<ending>합니다|한다|해라|해|하세요|하십시오)?(?<punct>[.!?…]*)",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex StatPointsObjectRegex = new(
        pattern: @"(?<stat>체력|매지카|지구력)\s*포인트를",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex AdjacentNumericBeforeDurationPhraseRegex = new(
        pattern: @"(?<points>[+-]?<\s*[0-9]+\s*>)(?:\s*포인트)?\s*(?<dur>[+-]?<\s*[0-9]+\s*>)\s*초\s*(?<time>동안|간)\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex PointSecondPointGarbageRegex = new(
        pattern: @"포인트\s*초\s*포인트",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex DuplicatePointsRegex = new(
        pattern: @"포인트\s*포인트",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    public string Apply(KoreanFixContext context, string text)
    {
        var working = text;

        // Fix: "투명화하기 상태" -> "투명화 상태"
        if (working.IndexOf("하기", StringComparison.Ordinal) >= 0 && working.IndexOf("상태", StringComparison.Ordinal) >= 0)
        {
            working = HaGiBeforeStateNounRegex.Replace(
                working,
                m => m.Groups["stem"].Value
            );
        }

        // Fix: "투명화하기 합니다" -> "투명화합니다"
        if (working.IndexOf("하기", StringComparison.Ordinal) >= 0 && working.IndexOf("합니다", StringComparison.Ordinal) >= 0)
        {
            working = HaGiBeforeHapNidaRegex.Replace(
                working,
                m => m.Groups["stem"].Value + "합니다"
            );
        }

        // Fix: "무기를 물건 전달!" -> "무기를 내려!"
        if (working.IndexOf("무기", StringComparison.Ordinal) >= 0 && working.IndexOf("물건", StringComparison.Ordinal) >= 0)
        {
            working = WeaponGoodsDropArtifactRegex.Replace(
                working,
                m =>
                {
                    var ending = m.Groups["ending"].Value;
                    var punct = m.Groups["punct"].Value;

                    return ending switch
                    {
                        "하십시오" => "무기를 내리십시오" + punct,
                        "하세요" => "무기를 내리세요" + punct,
                        "합니다" => "무기를 내리십시오" + punct,
                        "한다" => "무기를 내려라" + punct,
                        "해라" => "무기를 내려라" + punct,
                        _ => "무기를 내려" + punct,
                    };
                }
            );
        }

        // Fix: "지구력포인트를 흡수" / "체력포인트를 흡수" -> "지구력을/체력을 흡수"
        if (working.IndexOf("포인트를", StringComparison.Ordinal) >= 0)
        {
            working = StatPointsObjectRegex.Replace(
                working,
                m =>
                {
                    var stat = m.Groups["stat"].Value;
                    return stat + KoreanParticleSelector.ChooseObjectParticle(stat);
                }
            );
        }

        // Fix: "<7><3>초 동안 ..." -> "<3>초 동안 <7>포인트 ..."
        if (working.IndexOf('<') >= 0 && working.IndexOf("초", StringComparison.Ordinal) >= 0 && working.IndexOf("동안", StringComparison.Ordinal) >= 0)
        {
            working = AdjacentNumericBeforeDurationPhraseRegex.Replace(
                working,
                m => m.Groups["dur"].Value.Trim() + "초 " + m.Groups["time"].Value + " " + m.Groups["points"].Value.Trim() + "포인트"
            );
        }

        // Fix unit garbage like "포인트초포인트".
        if (working.IndexOf("포인트", StringComparison.Ordinal) >= 0 && working.IndexOf("초", StringComparison.Ordinal) >= 0)
        {
            working = PointSecondPointGarbageRegex.Replace(working, "포인트");
            working = DuplicatePointsRegex.Replace(working, "포인트");
        }

        return working;
    }
}
