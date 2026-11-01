using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string? TryFixBadMagDurUsageTemplates(string trimmedSource, string dest)
    {
        return TryFixBadMagDurUsageTemplates_Common(trimmedSource, dest)
            ?? TryFixBadMagDurUsageTemplates_SignedAmount(trimmedSource, dest);
    }

    private static string? TryFixBadMagDurUsageTemplates_Common(string trimmedSource, string dest)
    {
        return TryFixBadMagDurUsageTemplates_Regen(trimmedSource, dest)
            ?? TryFixBadMagDurUsageTemplates_CarryDealPoints(trimmedSource, dest);
    }

    private static string? TryFixBadMagDurUsageTemplates_Regen(string trimmedSource, string dest)
    {
        var regen = RegenSourceRegex.Match(trimmedSource);
        if (!regen.Success)
        {
            return null;
        }

        if (!LooksLikeBadMagDurUsage(dest))
        {
            return dest;
        }

        var attr = regen.Groups["attr"].Value.Trim();
        var magToken = regen.Groups["mag"].Value.Trim();
        var durToken = regen.Groups["dur"].Value.Trim();
        var speed = regen.Groups["speed"].Value.Trim();
        var attrKo = KnownSubjectsKo.TryGetValue(attr, out var ko) ? ko : attr;
        var speedKo = speed.Equals("faster", StringComparison.OrdinalIgnoreCase) ? "빨라집니다" : "느려집니다";
        return $"{durToken}초 동안 {attrKo} 재생 속도가 {magToken}% {speedKo}.";
    }

    private static string? TryFixBadMagDurUsageTemplates_CarryDealPoints(string trimmedSource, string dest)
    {
        var carry = CarryWeightReducedRegex.Match(trimmedSource);
        if (carry.Success)
        {
            return LooksLikeBadMagDurUsage(dest)
                ? $"{carry.Groups["dur"].Value.Trim()}초 동안 무게 한계가 {carry.Groups["mag"].Value.Trim()}만큼 감소합니다."
                : dest;
        }

        var deal = DealDamageDuringRegex.Match(trimmedSource);
        if (deal.Success)
        {
            return LooksLikeBadMagDurUsage(dest)
                ? $"{deal.Groups["dur"].Value.Trim()}초 동안 {deal.Groups["mag"].Value.Trim()}의 피해를 줍니다."
                : dest;
        }

        var pts = PointsStrongerRegex.Match(trimmedSource);
        if (pts.Success)
        {
            if (!LooksLikeBadMagDurUsage(dest))
            {
                return dest;
            }

            var skill = pts.Groups["skill"].Value.Trim();
            var magToken = pts.Groups["mag"].Value.Trim();
            var durToken = pts.Groups["dur"].Value.Trim();
            // A skill the table does not know stays the translation's: the template wrote its English name into the line.
            return KnownSubjectsKo.TryGetValue(skill, out var skillKo)
                ? $"{durToken}초 동안 {skillKo}이(가) {magToken}포인트 더 강해집니다."
                : dest;
        }

        return null;
    }

    private static string? TryFixBadMagDurUsageTemplates_SignedAmount(string trimmedSource, string dest)
    {
        var signed = SignedAmountForDurationRegex.Match(trimmedSource);
        if (!signed.Success)
        {
            return null;
        }

        var mag = signed.Groups["mag"].Value.Trim();
        var subject = signed.Groups["subject"].Value.Trim();
        var dur = signed.Groups["dur"].Value.Trim();
        // Only a subject the table knows: "+<mag> per second for <dur> seconds" became "<dur>초 동안 per second <mag>.",
        // its English in the Korean line.
        return KnownSubjectsKo.TryGetValue(subject, out var subjectKo) ? $"{dur}초 동안 {subjectKo} {mag}." : dest;
    }
}
