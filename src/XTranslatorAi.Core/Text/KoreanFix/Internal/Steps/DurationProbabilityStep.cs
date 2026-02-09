using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

internal sealed class DurationProbabilityStep : IKoreanFixStep
{
    private const string ParticleBoundary = @"(?=$|[\s\p{P}])";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex RawDurationMisplacementWithEuiRegex = new(
        pattern: @"(?<subject>[\p{L}][\p{L}\p{N} \-'\u2019]{0,40})\s*초\s*동안\s*(?<dur>[+-]?<\s*(?:dur|[0-9]+)\s*>)\s*초?\s*의",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex RawDurationMisplacementRegex = new(
        pattern: @"(?<subject>[\p{L}][\p{L}\p{N} \-'\u2019]{0,40})\s*초\s*동안\s*(?<dur>[+-]?<\s*(?:dur|[0-9]+)\s*>)\s*초?\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeout: RegexTimeout
    );

    private static readonly Regex MisplacedProbabilityAfterDurationRegex = new(
        pattern: @"(?<chance>(?:[+-]?<\s*[0-9]+(?:\.[0-9]+)?\s*%\s*>(?:\s*%)*|\b[0-9]+(?:\.[0-9]+)?\s*%))\s+"
                 + @"(?<duration>(?:[+-]?<[^>]+>|\b[0-9]+(?:\.[0-9]+)?\b)\s*초(?:\s*(?:동안|간))?)\s+"
                 + @"확률로"
                 + ParticleBoundary,
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeout: RegexTimeout
    );

    public string Apply(KoreanFixContext context, string text)
    {
        var working = text;

        // Fix: "늑대인간초 동안 <150>초 의 ..." => "<150>초 동안 늑대인간의 ..."
        if (working.IndexOf('<') >= 0 && working.IndexOf("동안", StringComparison.Ordinal) >= 0)
        {
            working = RawDurationMisplacementWithEuiRegex.Replace(
                working,
                m => m.Groups["dur"].Value.Trim() + "초 동안 " + m.Groups["subject"].Value.Trim() + "의"
            );
            working = RawDurationMisplacementRegex.Replace(
                working,
                m => m.Groups["dur"].Value.Trim() + "초 동안 " + m.Groups["subject"].Value.Trim()
            );
        }

        // Fix: "<25%> <5>초 동안 확률로 ..." => "<25%> 확률로 <5>초 동안 ..."
        if (working.IndexOf('%') >= 0 && working.IndexOf("확률로", StringComparison.Ordinal) >= 0 && working.IndexOf("초", StringComparison.Ordinal) >= 0)
        {
            working = MisplacedProbabilityAfterDurationRegex.Replace(
                working,
                m => m.Groups["chance"].Value.Trim() + " 확률로 " + m.Groups["duration"].Value.Trim()
            );
        }

        return working;
    }
}
