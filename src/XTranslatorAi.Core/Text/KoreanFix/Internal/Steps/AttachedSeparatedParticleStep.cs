using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

// Particles attached to a Latin word are left as the model wrote them. Korean readers choose them by
// how the word is read, not by its last letter: NPC는 (엔피시), HP가, DLC를, Enter를 (엔터), Nexus를
// (넥서스), Rune을 (룬). Rewriting by the last letter turned these into NPC은, HP이, DLC을 and Rune를.
internal sealed class AttachedSeparatedParticleStep : IKoreanFixStep
{
    private const string ParticleBoundary = @"(?=$|[\s\p{P}])";
    private const string LatinNoun = @"(?<noun>[A-Z][A-Za-z0-9 \-'\u2019]{1,40})";

    private static readonly Regex AttachedObjectParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})(?<particle>을|를)" + ParticleBoundary,
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

    // A lone "은" is often the noun "silver" ("은 주화"), so only the unambiguous "는" is joined.
    private static readonly Regex SeparatedTopicParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{1,30})\s+(?<particle>는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex SeparatedTopicParticleLatinRegex = new(
        pattern: LatinNoun + @"\s+(?<particle>는)" + ParticleBoundary,
        options: RegexOptions.CultureInvariant
    );

    // Countless Korean words end in "가" or "이" (무언가, 전문가, 기꺼이, 가까이, 무엇인가?), so a
    // Hangul subject particle is only corrected after the stat nouns of effect descriptions.
    // Particles after glossary terms are fixed where the term is substituted.
    private static readonly Regex AttachedSubjectParticleRegex = new(
        pattern: @"(?<noun>체력|매지카|지구력)(?<particle>이|가)" + ParticleBoundary,
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

            working = SeparatedObjectParticleLatinRegex.Replace(working, JoinLatinParticle);
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

            working = SeparatedTopicParticleLatinRegex.Replace(working, JoinLatinParticle);

            // Some model outputs duplicate topic particles after pronouns: "저는은", "나는은", ...
            working = DuplicatePronounTopicParticleRegex.Replace(
                working,
                m => m.Groups["pronoun"].Value
            );
        }

        // Fix wrong subject particle: "지구력가" -> "지구력이", "매지카이" -> "매지카가".
        // Spaced forms are left alone: a lone "이" is usually the demonstrative ("그대가 이 책")
        // and a lone "가" the verb ("그만 가 봐").
        if (working.IndexOf('이') >= 0 || working.IndexOf('가') >= 0)
        {
            working = AttachedSubjectParticleRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    return noun + KoreanParticleSelector.FixSubjectParticleSafely(noun, particle);
                }
            );
        }

        return working;
    }

    // A spaced particle after a Latin word is only joined, keeping the particle as written: "NPC 를" -> "NPC를".
    private static string JoinLatinParticle(Match m) => m.Groups["noun"].Value + m.Groups["particle"].Value;
}
