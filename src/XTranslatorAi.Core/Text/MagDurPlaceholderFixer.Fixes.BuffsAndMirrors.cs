using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string? TryFixFortifiesAndWeakensTemplates(string trimmedSource)
    {
        var fortifies = FortifiesSkillsAndAttrRegex.Match(trimmedSource);
        if (fortifies.Success)
        {
            return BuildFortifiesSkillsAndAttrKo(fortifies);
        }

        var weakens = WeakensArmorRatingRegex.Match(trimmedSource);
        if (weakens.Success)
        {
            var durToken = weakens.Groups["dur"].Value.Trim();
            var magToken = weakens.Groups["mag"].Value.Trim();
            return $"{durToken}초 동안 대상의 방어력이 {magToken}만큼 감소합니다.";
        }

        return null;
    }

    private static string? TryFixLossAndRecoveryMirrorTemplates(string trimmedSource)
    {
        var mirrorCaster = MirrorsCasterLossAndRecoveryRateRegex.Match(trimmedSource);
        if (mirrorCaster.Success)
        {
            var attr = mirrorCaster.Groups["attr"].Value.Trim();
            var dur = mirrorCaster.Groups["dur"].Value.Trim();
            var phraseKo = BuildLossAndRecoveryPhraseKo(attr);
            return $"{dur}초 동안 시전자의 {phraseKo} 속도를 대상과 동일하게 만듭니다.";
        }

        var mirrorAllies = MirrorsTargetLossAndRecoveryRateOnAlliesRegex.Match(trimmedSource);
        if (mirrorAllies.Success)
        {
            var attr = mirrorAllies.Groups["attr"].Value.Trim();
            var dur = mirrorAllies.Groups["dur"].Value.Trim();
            var phraseKo = BuildLossAndRecoveryPhraseKo(attr);
            return $"{dur}초 동안 대상의 {phraseKo} 속도가 주변 아군들에게도 동일하게 적용됩니다.";
        }

        var matchesLossRecovery = MatchesTargetLossAndRecoveryWithCasterRegex.Match(trimmedSource);
        if (matchesLossRecovery.Success)
        {
            var dur = matchesLossRecovery.Groups["dur"].Value.Trim();
            var attrTag = matchesLossRecovery.Groups["attrTag"].Value.Trim();
            var limitKo = StandardDoesNotWorkOnRegex.IsMatch(trimmedSource) ? " 언데드, 드래곤, 데이드라, 기계 장치에게는 효과가 없습니다." : "";
            return $"{dur}초 동안 대상의 {attrTag} 소모 및 회복 속도를 시전자와 동일하게 맞춥니다.{limitKo}";
        }

        var matchesLossAllies = MatchesTargetLossWithCasterWithAlliesRegex.Match(trimmedSource);
        if (matchesLossAllies.Success)
        {
            var dur = matchesLossAllies.Groups["dur"].Value.Trim();
            var targetTag = matchesLossAllies.Groups["targetTag"].Value.Trim();
            var attrTag = matchesLossAllies.Groups["attrTag"].Value.Trim();
            var limitKo = StandardDoesNotWorkOnRegex.IsMatch(trimmedSource) ? " 언데드, 드래곤, 데이드라, 기계 장치에게는 효과가 없습니다." : "";
            return $"{dur}초 동안 {targetTag} {attrTag} 소모 속도를 시전자와 동일하게 맞추며, 주변 아군에게도 적용됩니다.{limitKo}";
        }

        return null;
    }
}
