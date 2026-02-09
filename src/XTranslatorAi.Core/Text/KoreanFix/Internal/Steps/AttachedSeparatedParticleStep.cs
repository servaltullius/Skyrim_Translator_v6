using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class AttachedSeparatedParticleStep : IKoreanFixStep
{
    private const string ParticleBoundary = @"(?=$|[\s\p{P}])";
    private const string LatinNoun = @"(?<noun>[A-Z][A-Za-z0-9 \-'\u2019]{1,40})";

    private static readonly Regex AttachedObjectParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})(?<particle>을|를)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex AttachedObjectParticleLatinRegex = new(
        pattern: LatinNoun + @"(?<particle>을|를)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex SeparatedObjectParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s+(?<particle>을|를)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex SeparatedObjectParticleLatinRegex = new(
        pattern: LatinNoun + @"\s+(?<particle>을|를)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex AttachedTopicParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})(?<particle>은|는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex AttachedTopicParticleLatinRegex = new(
        pattern: LatinNoun + @"(?<particle>은|는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex SeparatedTopicParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s+(?<particle>은|는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex SeparatedTopicParticleLatinRegex = new(
        pattern: LatinNoun + @"\s+(?<particle>은|는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex DuplicatePronounTopicParticleRegex = new(
        pattern: @"(?<pronoun>저는|나는|너는|그는|그녀는|우리는|너희는|여러분은|당신은)(?:은|는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    public string Apply(KoreanFixContext context, string text)
    {
        var working = text;

        // Fix wrong particle choice on Hangul nouns: "매지카을" -> "매지카를", "검를" -> "검을".
        // Keep it narrow: Hangul-only nouns and a strict word boundary after the particle.
        if (working.IndexOf('을') >= 0 || working.IndexOf('를') >= 0)
        {
            working = SeparatedObjectParticleRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixObjectParticleSafely(noun, particle);
                }
            );

            working = AttachedObjectParticleRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixObjectParticleSafely(noun, particle);
                }
            );

            working = SeparatedObjectParticleLatinRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixObjectParticleSafelyLatin(noun, particle);
                }
            );

            working = AttachedObjectParticleLatinRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixObjectParticleSafelyLatin(noun, particle);
                }
            );
        }

        if (working.IndexOf('은') >= 0 || working.IndexOf('는') >= 0)
        {
            working = SeparatedTopicParticleRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixTopicParticleSafely(noun, particle);
                }
            );

            working = AttachedTopicParticleRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixTopicParticleSafely(noun, particle);
                }
            );

            working = SeparatedTopicParticleLatinRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixTopicParticleSafelyLatin(noun, particle);
                }
            );

            working = AttachedTopicParticleLatinRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixTopicParticleSafelyLatin(noun, particle);
                }
            );

            // Some model outputs duplicate topic particles after pronouns: "저는은", "나는은", ...
            working = DuplicatePronounTopicParticleRegex.Replace(
                working,
                m => m.Groups["pronoun"].Value
            );
        }

        return working;
    }
}
