using System;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string? TryFixProjectileAndBeamTemplates(string trimmedSource)
    {
        return TryFixProjectileAndBeamTemplates_Beams(trimmedSource)
            ?? TryFixProjectileAndBeamTemplates_Projectiles(trimmedSource)
            ?? TryFixProjectileAndBeamTemplates_Misc(trimmedSource);
    }

    private static string? TryFixProjectileAndBeamTemplates_Beams(string trimmedSource)
    {
        var streamCold = StreamOfColdDamagePerSecondToHealthAndStaminaRegex.Match(trimmedSource);
        if (streamCold.Success)
        {
            var mag = streamCold.Groups["mag"].Value.Trim();
            return $"체력과 지구력에 초당 {mag}포인트의 피해를 주는 냉기 줄기를 발사합니다.";
        }

        var rayFire = RayOfFireDamagePerSecondExtraDamageRegex.Match(trimmedSource);
        if (rayFire.Success)
        {
            var mag = rayFire.Groups["mag"].Value.Trim();
            return $"초당 {mag}포인트의 피해를 주는 화염 광선을 발사합니다. 불타는 대상은 추가 피해를 받습니다.";
        }

        var lightningBeam = LightningBeamShockDamageToHealthAndMagickaPerSecondRegex.Match(trimmedSource);
        if (lightningBeam.Success)
        {
            var mag = lightningBeam.Groups["mag"].Value.Trim();
            return $"체력과 매지카에 초당 {mag}포인트의 전격 피해를 주는 번개 광선을 발사합니다.";
        }

        var steam = BurstOfSteamDamagePerSecondRegex.Match(trimmedSource);
        if (steam.Success)
        {
            var mag = steam.Groups["mag"].Value.Trim();
            return $"초당 {mag}포인트의 피해를 주는 증기 폭발을 일으킵니다.";
        }

        return null;
    }

    private static string? TryFixProjectileAndBeamTemplates_Projectiles(string trimmedSource)
    {
        var takeDamage = TargetsTakeDamageForSecondsRegex.Match(trimmedSource);
        if (takeDamage.Success)
        {
            var mag = takeDamage.Groups["mag"].Value.Trim();
            var dur = takeDamage.Groups["dur"].Value.Trim();
            return $"{dur}초 동안 대상은 {mag}포인트의 피해를 입습니다.";
        }

        var seismic = SeismicWaveStaggersEnemiesDealingDamageRegex.Match(trimmedSource);
        if (seismic.Success)
        {
            var mag = seismic.Groups["mag"].Value.Trim();
            return $"앞에 있는 적들을 비틀거리게 하는 지진파를 일으켜 {mag}포인트의 피해를 줍니다.";
        }

        var rocks = PileOfThrownRocksDamageAndStaggersRegex.Match(trimmedSource);
        if (rocks.Success)
        {
            var mag = rocks.Groups["mag"].Value.Trim();
            return $"던져진 바위 더미가 {mag}포인트의 피해를 주고 적을 비틀거리게 합니다.";
        }

        var spear = SpearOfStoneDamageAndStaggersRegex.Match(trimmedSource);
        if (spear.Success)
        {
            var mag = spear.Groups["mag"].Value.Trim();
            return $"돌 창이 {mag}포인트의 피해를 주고 적을 비틀거리게 합니다.";
        }

        return null;
    }

    private static string? TryFixProjectileAndBeamTemplates_Misc(string trimmedSource)
    {
        var shockExplode = ShockingExplosionCenteredOnCasterRegex.Match(trimmedSource);
        if (shockExplode.Success)
        {
            var mag = shockExplode.Groups["mag"].Value.Trim();
            return $"시전자 중심으로 {mag}포인트의 전격 폭발을 일으킵니다. 가까이 있는 대상일수록 더 큰 피해를 받습니다.";
        }

        var breath = FieryBreathForMagDamagesRegex.Match(trimmedSource);
        if (breath.Success)
        {
            var mag = breath.Groups["mag"].Value.Trim();
            return $"화염 숨결을 내뿜어 {mag}의 피해를 줍니다.";
        }

        var dwarfClose = DamagesCloseDwarvenTargetsByMagPointsRegex.Match(trimmedSource);
        if (dwarfClose.Success)
        {
            var mag = dwarfClose.Groups["mag"].Value.Trim();
            return $"근처의 드워머(드워프) 자동기계에게 {mag}포인트의 피해를 줍니다.";
        }

        var poison = CausesPoisonDamageOnNonDwarvenTargetsForSecondsRegex.Match(trimmedSource);
        if (poison.Success)
        {
            var mag = poison.Groups["mag"].Value.Trim();
            var dur = poison.Groups["dur"].Value.Trim();
            return $"{dur}초 동안 드워머(드워프) 자동기계를 제외한 대상에게 {mag}포인트의 독 피해를 줍니다.";
        }

        var frozen = FrozenBetweenOblivionAndTamrielRegex.Match(trimmedSource);
        if (frozen.Success)
        {
            var dur = frozen.Groups["dur"].Value.Trim();
            return $"{dur}초 동안 대상은 오블리비언과 탐리엘 사이에 얼어붙어 모든 피해에 면역이 됩니다.";
        }

        var lightningBolt = LightningBoltShockDamageHalfToMagickaRegex.Match(trimmedSource);
        if (lightningBolt.Success)
        {
            var mag = lightningBolt.Groups["mag"].Value.Trim();
            var hasLeaps = lightningBolt.Groups["leaps"].Success && !string.IsNullOrWhiteSpace(lightningBolt.Groups["leaps"].Value);
            return hasLeaps
                ? $"체력에 {mag}포인트의 전격 피해를 주고, 매지카에는 그 절반의 피해를 준 뒤 새로운 대상에게 전이되는 번개 화살을 발사합니다."
                : $"체력에 {mag}포인트의 전격 피해를 주고, 매지카에는 그 절반의 피해를 주는 번개 화살을 발사합니다.";
        }

        return null;
    }
}
