using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static string TryFixNumericDurationTemplates(string source, string dest)
    {
        var trimmedSource = source.Trim();

        return TryFixNumericDurationTemplates_ActionSurge(trimmedSource)
            ?? TryFixNumericDurationTemplates_BerserkAndScaling(trimmedSource)
            ?? TryFixNumericDurationTemplates_AttackBuffs(trimmedSource)
            ?? TryFixNumericDurationTemplates_HealsAndDrains(trimmedSource)
            ?? TryFixNumericDurationTemplates_ReorderFormDurationToken(trimmedSource, dest)
            ?? dest;
    }

    private static string? TryFixNumericDurationTemplates_ActionSurge(string trimmedSource)
    {
        var ignores = IgnoresPercentPhysicalAndElementalDamageAfterAttackedSourceRegex.Match(trimmedSource);
        if (ignores.Success)
        {
            var pct = ignores.Groups["pct"].Value.Trim();
            var dur = ignores.Groups["dur"].Value.Trim();
            var cd = ignores.Groups["cd"].Value.Trim();
            return $"공격받은 후 {dur}초 동안 모든 물리 피해와 원소 피해의 {pct}%를 무시합니다. 이 효과의 재사용 대기시간은 {cd}초입니다.";
        }

        var staggerImmune = ChanceImmuneStaggerWhenAshOfWarCastSourceRegex.Match(trimmedSource);
        if (staggerImmune.Success)
        {
            var pct = staggerImmune.Groups["pct"].Value.Trim();
            return $"전회 시전 시 {pct}% 확률로 비틀거림에 면역이 됩니다.";
        }

        var restoreStam = WeaponArtRestoresStaminaDuringActionSurgeSourceRegex.Match(trimmedSource);
        if (restoreStam.Success)
        {
            var mag = restoreStam.Groups["mag"].Value.Trim();
            return $"액션 서지 동안 무기 전기 시전 시 지구력을 {mag}포인트 회복합니다.";
        }

        var reduced = DamageTakenReducedDuringActionSurgeSourceRegex.Match(trimmedSource);
        if (reduced.Success)
        {
            var pct = reduced.Groups["pct"].Value.Trim();
            return $"액션 서지 동안 받는 피해가 {pct}% 감소합니다.";
        }

        var doMore = DoMoreDamageDuringActionSurgeCooldownSourceRegex.Match(trimmedSource);
        if (doMore.Success)
        {
            var pct = doMore.Groups["pct"].Value.Trim();
            return $"액션 서지 재사용 대기시간 동안 피해가 {pct}% 증가합니다.";
        }

        return null;
    }

    private static string? TryFixNumericDurationTemplates_BerserkAndScaling(string trimmedSource)
    {
        var berserk = DealMorePhysicalDamageAndLoseHealthUntilBelowPercentSourceRegex.Match(trimmedSource);
        if (berserk.Success)
        {
            var dmgPct = berserk.Groups["dmgPct"].Value.Trim();
            var loss = berserk.Groups["loss"].Value.Trim();
            var threshold = berserk.Groups["threshold"].Value.Trim();
            return $"물리 피해가 {dmgPct}% 증가하지만, 체력이 {threshold}% 미만이 될 때까지 초당 체력이 {loss} 감소합니다. 이 효과로 인해 사망하지는 않습니다.";
        }

        var multiBonus = SpellsMoreEffectiveMagickaRegenFasterWeaponDamagePerLevelSourceRegex.Match(trimmedSource);
        if (multiBonus.Success)
        {
            var spellPct = multiBonus.Groups["spellPct"].Value.Trim();
            var regenPct = multiBonus.Groups["regenPct"].Value.Trim();
            var weaponPct = multiBonus.Groups["weaponPct"].Value.Trim();
            var skill = multiBonus.Groups["skill"].Value.Trim();
            var skillKo = TranslateKnownSubjectKo(skill);

            return $"주문 효과가 {spellPct}% 증가하고, 매지카 재생 속도가 {regenPct}% 빨라지며, {skillKo} 레벨당 무기 피해가 {weaponPct}% 증가합니다.";
        }

        return null;
    }

    private static string? TryFixNumericDurationTemplates_AttackBuffs(string trimmedSource)
    {
        var combatBuff = IncreasesArmorRatingAndMagicResistanceWhileAttackingSourceRegex.Match(trimmedSource);
        if (combatBuff.Success)
        {
            var armor = combatBuff.Groups["armor"].Value.Trim();
            var mr = combatBuff.Groups["mr"].Value.Trim();
            var dur = combatBuff.Groups["dur"].Value.Trim();
            var cd = combatBuff.Groups["cd"].Value.Trim();

            return $"공격 시 {dur}초 동안 방어력이 {armor}포인트, 마법 저항이 {mr}% 증가합니다. 이 효과는 {cd}초마다 한 번씩 발동합니다.";
        }

        var combatBuffNoCd = IncreasesArmorRatingAndMagicResistanceWhileAttackingNoCooldownSourceRegex.Match(trimmedSource);
        if (combatBuffNoCd.Success)
        {
            var armor = combatBuffNoCd.Groups["armor"].Value.Trim();
            var mr = combatBuffNoCd.Groups["mr"].Value.Trim();
            var dur = combatBuffNoCd.Groups["dur"].Value.Trim();
            return $"공격 시 {dur}초 동안 방어력이 {armor}포인트, 마법 저항이 {mr}% 증가합니다.";
        }

        var buffForSeconds = IncreasesArmorRatingAndMagicResistanceForSecondsSourceRegex.Match(trimmedSource);
        if (buffForSeconds.Success)
        {
            var armor = buffForSeconds.Groups["armor"].Value.Trim();
            var mr = buffForSeconds.Groups["mr"].Value.Trim();
            var dur = buffForSeconds.Groups["dur"].Value.Trim();
            return $"{dur}초 동안 방어력이 {armor}포인트, 마법 저항이 {mr}% 증가합니다.";
        }

        return null;
    }

    private static string? TryFixNumericDurationTemplates_HealsAndDrains(string trimmedSource)
    {
        var healsDrains = HealsThenDrainsMagickaPerSecondSourceRegex.Match(trimmedSource);
        if (healsDrains.Success)
        {
            var healMag = healsDrains.Groups["healMag"].Value.Trim();
            var healDur = healsDrains.Groups["healDur"].Value.Trim();
            var drainMag = healsDrains.Groups["drainMag"].Value.Trim();
            var drainDur = healsDrains.Groups["drainDur"].Value.Trim();

            return $"{healDur}초 동안 체력을 초당 {healMag}포인트 회복합니다. {drainDur}초 동안 매지카가 초당 {drainMag}포인트씩 소모됩니다.";
        }

        var wontFightDeplete = WontFightThenMagickaStaminaDepletedSourceRegex.Match(trimmedSource);
        if (wontFightDeplete.Success)
        {
            var lvl = wontFightDeplete.Groups["lvl"].Value.Trim();
            var fightDur = wontFightDeplete.Groups["fightDur"].Value.Trim();
            var drainMag = wontFightDeplete.Groups["drainMag"].Value.Trim();
            var drainDur = wontFightDeplete.Groups["drainDur"].Value.Trim();

            return $"주변의 레벨 {lvl} 이하의 생명체와 사람들은 {fightDur}초 동안 싸우지 않습니다. {drainDur}초 동안 매지카와 지구력이 초당 {drainMag}포인트씩 소모됩니다.";
        }

        var absorbsBleed = AbsorbsHealthAndBleedForSecondsSourceRegex.Match(trimmedSource);
        if (absorbsBleed.Success)
        {
            var health = absorbsBleed.Groups["health"].Value.Trim();
            var bleed = absorbsBleed.Groups["bleed"].Value.Trim();
            var dur = absorbsBleed.Groups["dur"].Value.Trim();
            return $"{dur}초 동안 체력을 {health}포인트 흡수하고, {bleed}포인트의 출혈 피해를 줍니다.";
        }

        return null;
    }

    private static string? TryFixNumericDurationTemplates_ReorderFormDurationToken(string trimmedSource, string dest)
    {
        var takeOnForm = TakeOnFormForSecondsSourceRegex.Match(trimmedSource);
        if (!takeOnForm.Success)
        {
            return null;
        }

        var durToken = takeOnForm.Groups["dur"].Value.Trim();
        if (string.IsNullOrWhiteSpace(durToken))
        {
            return dest;
        }

        var escDur = Regex.Escape(durToken);
        if (Regex.IsMatch(dest, escDur + @"\s*(초간|초|분|시간|일|주|개월|년|동안|간)", RegexOptions.CultureInvariant))
        {
            return dest;
        }

        var pattern =
            @"(?<form>[\p{L}\p{N}][\p{L}\p{N} \-'\u2019]{0,40})\s*초\s*동안\s*"
            + escDur
            + @"\s*의";
        var replaced = Regex.Replace(
            dest,
            pattern,
            m => $"{durToken}초 동안 {m.Groups["form"].Value.Trim()}의",
            RegexOptions.CultureInvariant
        );

        return string.Equals(replaced, dest, StringComparison.Ordinal) ? dest : replaced;
    }
}

