using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string? TryFixRestoreAndBasicEffectTemplates(string trimmedSource)
    {
        var restore = RestoreAttrByPointsRegex.Match(trimmedSource);
        if (restore.Success)
        {
            var mag = restore.Groups["mag"].Value.Trim();
            var attr = restore.Groups["attr"].Value.Trim();
            var attrKo = TranslateKnownSubjectKo(attr);
            return $"{attrKo}{GetObjectParticle(attrKo)} {mag}포인트 회복합니다.";
        }

        var thunder = ThunderShockDamageHalfToMagickaRegex.Match(trimmedSource);
        if (thunder.Success)
        {
            var mag = thunder.Groups["mag"].Value.Trim();
            return $"체력에 {mag}포인트의 전격 피해를 주고, 매지카에는 그 절반의 피해를 주는 벼락입니다.";
        }

        var frost = TargetsTakeFrostDamageForSecondsPlusStaminaDamageRegex.Match(trimmedSource);
        if (frost.Success)
        {
            var mag = frost.Groups["mag"].Value.Trim();
            var dur = frost.Groups["dur"].Value.Trim();
            return $"{dur}초 동안 대상은 {mag}포인트의 냉기 피해를 입으며, 추가로 지구력 피해를 받습니다.";
        }

        var blast = BlastOfColdDamagePerSecondToHealthAndStaminaRegex.Match(trimmedSource);
        if (blast.Success)
        {
            var mag = blast.Groups["mag"].Value.Trim();
            return $"체력과 지구력에 초당 {mag}포인트의 냉기 피해를 주는 한기 폭발을 일으킵니다.";
        }

        var burnAttack = BurnsEnemyForSecondsDamageToHealthEverySecondRegex.Match(trimmedSource);
        if (burnAttack.Success)
        {
            var dur = burnAttack.Groups["dur"].Value.Trim();
            var mag = burnAttack.Groups["mag"].Value.Trim();
            return $"대상을 {dur}초 동안 불태워 초당 체력에 {mag}포인트의 피해를 줍니다.";
        }

        var lacerate = LaceratesEnemyBleedsForSecondsDealingDamageToHealthAndStaminaEverySecondRegex.Match(trimmedSource);
        if (lacerate.Success)
        {
            var dur = lacerate.Groups["dur"].Value.Trim();
            var mag = lacerate.Groups["mag"].Value.Trim();
            return $"적을 찢어 {dur}초 동안 출혈을 일으키며, 초당 체력과 지구력에 {mag}포인트의 피해를 줍니다.";
        }

        return null;
    }
}
