using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string? TryFixDrainAndCreatureTemplates(string trimmedSource)
    {
        var drains = DrainsAttrByPointsPerSecondRegex.Match(trimmedSource);
        if (drains.Success)
        {
            var isTarget = drains.Groups["target"].Success && !string.IsNullOrWhiteSpace(drains.Groups["target"].Value);
            var attr = drains.Groups["attr"].Value.Trim();
            var mag = drains.Groups["mag"].Value.Trim();
            var dur = drains.Groups["dur"].Value.Trim();

            var attrKo = TranslateKnownSubjectKo(attr);
            var noun = isTarget ? $"대상의 {attrKo}" : attrKo;
            return $"{dur}초 동안 {noun}{GetSubjectParticle(noun)} 초당 {mag}포인트씩 소모됩니다.";
        }

        var wontFight = CreaturesWontFightUpToLevelRegex.Match(trimmedSource);
        if (wontFight.Success)
        {
            var lvl = wontFight.Groups["lvl"].Value.Trim();
            var dur = wontFight.Groups["dur"].Value.Trim();
            return $"주변의 레벨 {lvl} 이하의 생명체와 사람들은 {dur}초 동안 싸우지 않습니다.";
        }

        var drainsOnce = DrainsMagPointsFromAttrRegex.Match(trimmedSource);
        if (drainsOnce.Success)
        {
            var mag = drainsOnce.Groups["mag"].Value.Trim();
            var attr = drainsOnce.Groups["attr"].Value.Trim();
            var attrKo = TranslateKnownSubjectKo(attr);
            return $"{attrKo}에서 {mag}포인트를 흡수합니다.";
        }

        return null;
    }

    private static string? TryFixCloakTemplates(string trimmedSource)
    {
        var absorbCloak = ForSecondsAbsorbOpponentsHealthDamagePerSecondRegex.Match(trimmedSource);
        if (absorbCloak.Success)
        {
            var dur = absorbCloak.Groups["dur"].Value.Trim();
            var mag = absorbCloak.Groups["mag"].Value.Trim();
            return $"{dur}초 동안 근접 범위 내의 적에게서 체력을 흡수하며, 초당 {mag}포인트의 피해를 줍니다.";
        }

        var frostCloak = ForSecondsMeleeOpponentsTakeFrostAndStaminaDamagePerSecondRegex.Match(trimmedSource);
        if (frostCloak.Success)
        {
            var dur = frostCloak.Groups["dur"].Value.Trim();
            var mag = frostCloak.Groups["mag"].Value.Trim();
            return $"{dur}초 동안 근접 범위 내의 적은 초당 {mag}포인트의 냉기 피해와 지구력 피해를 받습니다.";
        }

        var fireCloak = ForSecondsMeleeOpponentsTakeFireDamagePerSecondExtraDamageRegex.Match(trimmedSource);
        if (fireCloak.Success)
        {
            var dur = fireCloak.Groups["dur"].Value.Trim();
            var mag = fireCloak.Groups["mag"].Value.Trim();
            return $"{dur}초 동안 근접 범위 내의 적은 초당 {mag}포인트의 화염 피해를 받습니다. 불타는 대상은 추가 피해를 받습니다.";
        }

        return null;
    }
}
