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

    // Stats and skills as the built-in glossary names them. After any other word a lone "이" before a
    // number or tag is the demonstrative: "내가 이 7년 동안", "그에게 이 100골드를", "어서 이 <Alias=QuestItem>을"
    // became "내가가 7년", "그에게가 100골드를" and "어서가" when every word was accepted here.
    // 대장 (Smithing) is left out because it is also the vocative "boss" ("대장 이 100골드면 돼").
    private const string StatOrSkillNoun =
        "체력|매지카|지구력|한손무기|양손무기|궁술|막기|방어|중갑|경갑|제련|은신|소매치기|잠금해제|화술|연금술|마법부여"
        + "|변화마법|소환마법|파괴마법|환영마법|회복마법";

    private static readonly Regex SeparatedSubjectParticleRegex = new(
        pattern: @"(?<noun>" + StatOrSkillNoun + @")\s+(?<particle>가|이)\s+(?=[+-]?(?:<(?i:mag|dur)>|<[0-9]|[0-9]))",
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

        // Fix spaced subject particles in short stat phrases before <mag>, <dur> or a number:
        // "중갑 가 <mag>" -> "중갑이 <mag>".
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
