using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class ParenthesizedParticleStep : IKoreanFixStep
{
    private const string LatinNoun = @"(?<noun>[A-Z][A-Za-z0-9 \-'\u2019]{1,40})";

    private static readonly Regex ParenthesizedObjectParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s*(?:을\(를\)|를\(을\)|\(\s*을\s*\)\s*를|\(\s*를\s*\)\s*을|을/를|를/을)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex ParenthesizedObjectParticleLatinRegex = new(
        pattern: LatinNoun + @"\s*(?:을\(를\)|를\(을\)|\(\s*을\s*\)\s*를|\(\s*를\s*\)\s*을|을/를|를/을)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex ParenthesizedTopicParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s*(?:은\(는\)|는\(은\)|\(\s*은\s*\)\s*는|\(\s*는\s*\)\s*은|은/는|는/은)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex ParenthesizedTopicParticleLatinRegex = new(
        pattern: LatinNoun + @"\s*(?:은\(는\)|는\(은\)|\(\s*은\s*\)\s*는|\(\s*는\s*\)\s*은|은/는|는/은)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex ParenthesizedSubjectParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s*(?:이\(가\)|가\(이\)|\(\s*이\s*\)\s*가|\(\s*가\s*\)\s*이|이/가|가/이)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex ParenthesizedConjunctionParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s*(?:과\(와\)|와\(과\)|\(\s*와\s*\)\s*과|\(\s*과\s*\)\s*와|과/와|와/과)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex ParenthesizedDirectionalParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s*(?:\(\s*으\s*\)\s*로|으로\(로\)|로\(으로\)|으로/로|로/으로)",
        options: RegexOptions.CultureInvariant
    );

    public string Apply(KoreanFixContext context, string text)
    {
        if (text.IndexOf('(') < 0 && text.IndexOf('/') < 0)
        {
            return text;
        }

        var working = text;

        working = ParenthesizedObjectParticleRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseObjectParticle(noun);
            }
        );

        working = ParenthesizedObjectParticleLatinRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseObjectParticleLatin(noun);
            }
        );

        working = ParenthesizedTopicParticleRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseTopicParticle(noun);
            }
        );

        working = ParenthesizedTopicParticleLatinRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseTopicParticleLatin(noun);
            }
        );

        working = ParenthesizedSubjectParticleRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseSubjectParticle(noun);
            }
        );

        working = ParenthesizedConjunctionParticleRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseConjunctionParticle(noun);
            }
        );

        working = ParenthesizedDirectionalParticleRegex.Replace(
            working,
            m =>
            {
                var noun = m.Groups["noun"].Value;
                return noun + KoreanParticleSelector.ChooseDirectionalParticle(noun);
            }
        );

        return working;
    }
}
