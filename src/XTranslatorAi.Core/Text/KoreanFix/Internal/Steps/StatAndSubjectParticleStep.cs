using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class StatAndSubjectParticleStep : IKoreanFixStep
{
    private const string ParticleBoundary = @"(?=$|[\s\p{P}])";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex StatDativeParticleFromRegex = new(
        pattern: @"(?<stat>체력|매지카|지구력)에게서",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex StatDativeParticleRegex = new(
        pattern: @"(?<stat>체력|매지카|지구력)에게(?<suffix>는|도|만|까지|부터)?" + ParticleBoundary,
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex SeparatedSubjectParticleRegex = new(
        pattern: @"(?<noun>[가-힣]{2,20})\s+(?<particle>가|이)\s+(?=(?:[+-]?<|\b[0-9]))",
        options: RegexOptions.CultureInvariant,
        matchTimeout: RegexTimeout
    );

    public string Apply(KoreanFixContext context, string text)
    {
        var working = text;

        // "체력에게/매지카에게/지구력에게" is almost always wrong in this domain.
        if (working.IndexOf("에게", StringComparison.Ordinal) >= 0)
        {
            working = StatDativeParticleFromRegex.Replace(working, m => m.Groups["stat"].Value + "에서");
            working = StatDativeParticleRegex.Replace(working, m => m.Groups["stat"].Value + "에" + m.Groups["suffix"].Value);
        }

        // Fix spaced subject particles in short stat phrases: "중갑 가 <mag>" -> "중갑이 <mag>".
        if (working.IndexOf(' ') >= 0)
        {
            working = SeparatedSubjectParticleRegex.Replace(
                working,
                m =>
                {
                    var noun = m.Groups["noun"].Value;
                    var particle = m.Groups["particle"].Value;
                    var corrected = KoreanParticleSelector.ChooseSubjectParticle(noun);
                    if (string.Equals(particle, corrected, StringComparison.Ordinal))
                    {
                        return noun + particle + " ";
                    }

                    return noun + corrected + " ";
                }
            );
        }

        return working;
    }
}
