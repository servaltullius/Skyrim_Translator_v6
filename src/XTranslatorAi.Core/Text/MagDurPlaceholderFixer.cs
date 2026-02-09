using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static partial class MagDurPlaceholderFixer
{
    private static readonly Regex MagRegex = new(@"[+-]?<\s*mag\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex DurRegex = new(@"[+-]?<\s*dur\s*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex StandardDoesNotWorkOnRegex = new(
        pattern: @"\bdoes\s+not\s+work\s+on\s+undead,\s*dragons,\s*daedra(?:,)?\s*or\s*machines\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DuplicateSignedPlaceholderRegex = new(
        pattern: @"(?:(?<plus>\+)\s*(?<plus2>\+)\s*(?<mag><\s*mag\s*>)|(?<minus>-)\s*(?<minus2>-)\s*(?<mag2><\s*mag\s*>))",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex RegenSourceRegex = new(
        pattern: @"^(?<attr>Health|Magicka|Stamina)\s+regenerates\s+(?<mag><\s*mag\s*>)%\s+(?<speed>faster|slower)\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex CarryWeightReducedRegex = new(
        pattern: @"^Carry\s+weight\s+is\s+reduced\s+by\s+(?<mag><\s*mag\s*>)\s+for\s+(?<dur><\s*dur\s*>)\.?\s*$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DealDamageDuringRegex = new(
        pattern: @"^Deal\s+(?<mag><\s*mag\s*>)\s+damage(s)?\s+during\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DrainsAttrByPointsPerSecondRegex = new(
        pattern: @"^Drains\s+(?<target>the\s+target.?s\s+)?(?<attr>Health|Magicka|Stamina)\s+by\s+(?<mag><\s*mag\s*>)\s+points?\s+per\s+second\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DrainsMagPointsFromAttrRegex = new(
        pattern: @"^Drains\s+(?<mag><\s*mag\s*>)\s+points?\s+from\s+(?<attr>Health|Magicka|Stamina)\.?\s*$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex CreaturesWontFightUpToLevelRegex = new(
        pattern: @"^Creatures\s+and\s+people\s+up\s+to\s+level\s+(?<lvl><\s*mag\s*>)\s+nearby\s+won['\u2019]t\s+fight\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ForSecondsAbsorbOpponentsHealthDamagePerSecondRegex = new(
        pattern: @"^For\s+(?<dur><\s*dur\s*>)\s+seconds,\s+you\s+absorb\s+your\s+opponent(?:s)?['\u2019](?:s)?\s+health\s+within\s+melee\s+range,\s+dealing\s+(?<mag><[^>]+>)\s+points?\s+of\s+damage\s+per\s+second\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ForSecondsMeleeOpponentsTakeFrostAndStaminaDamagePerSecondRegex = new(
        pattern: @"^For\s+(?<dur><\s*dur\s*>)\s+seconds,\s+opponents\s+in\s+melee\s+range\s+take\s+(?<mag><[^>]+>)\s+points?\s+frost\s+damage\s+and\s+Stamina\s+damage\s+per\s+second\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ForSecondsMeleeOpponentsTakeFireDamagePerSecondExtraDamageRegex = new(
        pattern: @"^For\s+(?<dur><\s*dur\s*>)\s+seconds,\s+opponents\s+in\s+melee\s+range\s+take\s+(?<mag><[^>]+>)\s+points?\s+fire\s+damage\s+per\s+second\.\s*Targets\s+on\s+fire\s+take\s+extra\s+damage\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex AbsorbAttrPerSecondFromTargetRegex = new(
        pattern: @"^Absorb\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+(?<attr>Health|Magicka|Stamina)\s+per\s+second\s+from\s+the\s+target\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex PointsStrongerRegex = new(
        pattern: @"^(?<skill>[A-Za-z][A-Za-z \-']*?)\s+is\s+(?<mag><\s*mag\s*>)\s+points?\s+stronger\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex FortifiesSkillsAndAttrRegex = new(
        pattern: @"^Forti?f(?:y|ies)\s+(?<skills>.+?)\s+(?:skills?\s+)?(?:by|is)\s+(?<mag>[+-]?<\s*(?:mag|\d+)\s*>)(?<magPoints>\s+points?)?\s*(?:,)?\s*(?:and|as\s+well\s+as)\s+(?<attr>Health|Magicka|Stamina)\s+(?:by\s+)?(?<attrMag>[+-]?<\s*(?:mag|\d+)\s*>)(?<attrPoints>\s+points?)?\s+for\s+(?<dur>[+-]?<\s*(?:dur|\d+)\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex WeakensArmorRatingRegex = new(
        pattern: @"^Weakens\s+the\s+target.?s\s+armor\s+rating\s+by\s+(?:a\s+strength\s+of\s+)?(?<mag><\s*mag\s*>)\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex MirrorsCasterLossAndRecoveryRateRegex = new(
        pattern: @"^Mirrors\s+the\s+caster.?s\s+rate\s+of\s+(?<attr>Health|Magicka|Stamina)\s+loss\s+and\s+recovery\s+with\s+the\s+target.?s\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex MirrorsTargetLossAndRecoveryRateOnAlliesRegex = new(
        pattern: @"^The\s+target.?s\s+rate\s+of\s+(?<attr>Health|Magicka|Stamina)\s+loss\s+and\s+recovery\s+is\s+mirrored\s+on\s+(?:its\s+)?nearby\s+allies\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex MatchesTargetLossAndRecoveryWithCasterRegex = new(
        pattern: @"^Matches\s+the\s+target.?s\s+rate\s+of\s+(?<attrTag><[^>]+>)\s+loss\s+and\s+recovery\s+with\s+(?:the\s+)?caster\s+for\s+(?<dur><\s*dur\s*>)(?:\s*seconds)?\s*,?\s*(?<tail>.+)?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex MatchesTargetLossWithCasterWithAlliesRegex = new(
        pattern: @"^Matches\s+the\s+(?<targetTag><[^>]+>)\s+rate\s+of\s+(?<attrTag><[^>]+>)\s+loss\s+with\s+(?:the\s+)?caster\s+for\s+(?<dur><\s*dur\s*>)(?:\s*seconds)?\s+with\s+nearby\s+all(?:y|ie)s\s*,?\s*(?<tail>.+)?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex TakeOnFormForSecondsSourceRegex = new(
        pattern: @"^Take\s+on\s+the\s+form\s+of\s+the\s+(?<form>.+?)\s+for\s+(?<dur><[^>]+>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex HealsThenDrainsMagickaPerSecondSourceRegex = new(
        pattern: @"^Heals\s+(?<healMag><[^>]+>)\s+points?\s+per\s+second\s+for\s+(?<healDur><\s*\d+\s*>)\s+seconds\.\s*Drains\s+magicka\s+by\s+(?<drainMag><[^>]+>)\s+points?\s+per\s+second\s+for\s+(?<drainDur><\s*\d+\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex WontFightThenMagickaStaminaDepletedSourceRegex = new(
        pattern: @"^Creatures\s+and\s+people\s+up\s+to\s+level\s+(?<lvl><\s*\d+\s*>)\s+nearby\s+won['\u2019]t\s+fight\s+for\s+(?<fightDur><\s*\d+\s*>)\s+seconds\.\s*Your\s+Magicka\s+and\s+Stamina\s+are\s+depleted\s+by\s+(?<drainMag><[^>]+>)\s+points?\s+per\s+second\s+for\s+(?<drainDur><\s*\d+\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex AbsorbsHealthAndBleedForSecondsSourceRegex = new(
        pattern: @"^Absorbs\s+(?<health><\s*\d+\s*>)\s+points?\s+of\s+health\s+and\s+does\s+(?<bleed><\s*\d+\s*>)\s+points?\s+of\s+bleeding\s+damage\s+for\s+(?<dur><\s*\d+\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex IgnoresPercentPhysicalAndElementalDamageAfterAttackedSourceRegex = new(
        pattern: @"^Ignores\s+(?<pct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+of\s+all\s+physical\s+damage\s+and\s+elemental\s+damage\s+for\s+(?<dur><[^>]+>)\s+seconds\s+after\s+being\s+attacked\.\s*This\s+effect\s+has\s+a\s+(?<cd>\d+|<[^>]+>)\s+seconds?\s+cooldown\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ChanceImmuneStaggerWhenAshOfWarCastSourceRegex = new(
        pattern: @"^(?<pct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+chance\s+to\s+(?:be\s+)?immune(?:\s+to)?\s+stagger\s+when\s+Ash\s+of\s+War\s+is\s+cast\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex WeaponArtRestoresStaminaDuringActionSurgeSourceRegex = new(
        pattern: @"^Casting\s+Weapon\s+Art\s+will\s+restores?\s+(?<mag>\d+|<[^>]+>)\s+points?\s+of\s+stamina\s+during\s+the\s+Action\s+Surge\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DamageTakenReducedDuringActionSurgeSourceRegex = new(
        pattern: @"^Damage\s+taken\s+is\s+reduced\s+(?<pct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+during\s+the\s+Action\s+Surge\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DoMoreDamageDuringActionSurgeCooldownSourceRegex = new(
        pattern: @"^Do\s+more\s+(?<pct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+damage\s+during\s+the\s+cooldown\s+of\s+Action\s+Surge\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DealMorePhysicalDamageAndLoseHealthUntilBelowPercentSourceRegex = new(
        pattern: @"^Deal\s+(?<dmgPct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+more\s+physical\s+damage\s+and\s+lose\s+(?<loss>\d+|<[^>]+>)\s+health\s+per\s+second\s+until\s+your\s+health\s+is\s+below\s+(?<threshold>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\.\s*You\s+will\s+not\s+be\s+killed\s+by\s+this\s+effect\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex SpellsMoreEffectiveMagickaRegenFasterWeaponDamagePerLevelSourceRegex = new(
        pattern: @"^Your\s+spell(?:s)?\s+(?:have|are)\s+(?:more\s+)?(?<spellPct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+effective,\s*magicka\s+regenerates\s+(?<regenPct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+faster,\s+and\s+your\s+weapon\s+damage\s+(?:are|is)\s+(?<weaponPct>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+more\s+powerful\s+per\s+level\s+of\s+(?<skill>[A-Za-z][A-Za-z \-']*)\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex IncreasesArmorRatingAndMagicResistanceWhileAttackingSourceRegex = new(
        pattern: @"^Increases\s+armor\s+rating\s+by\s+(?<armor>[+-]?<\s*\d+\s*>)\s+points?\s+and\s+magic\s+resistance\s+by\s+(?<mr>[+-]?<\s*\d+\s*>)%\s+while\s+attacking\s+for\s+(?<dur>[+-]?<\s*\d+\s*>)\s+seconds,\s+once\s+every\s+(?<cd>[+-]?<\s*\d+\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex IncreasesArmorRatingAndMagicResistanceWhileAttackingNoCooldownSourceRegex = new(
        pattern: @"^Increases\s+armor\s+rating\s+by\s+(?<armor>[+-]?<\s*\d+\s*>)\s+points?\s+and\s+magic\s+resistance\s+by\s+(?<mr>[+-]?<\s*\d+\s*>)%\s+while\s+attacking\s+for\s+(?<dur>[+-]?<\s*\d+\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex IncreasesArmorRatingAndMagicResistanceForSecondsSourceRegex = new(
        pattern: @"^Increases\s+armor\s+rating\s+by\s+(?<armor>[+-]?\d+|<[^>]+>)\s+points?\s+and\s+magic\s+resistance\s+by\s+(?<mr>[+-]?\d+(?:\.\d+)?|<[^>]+>)%\s+for\s+(?<dur>[+-]?\d+|<[^>]+>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ShockDamagePerSecondToHealthAndMagickaRegex = new(
        pattern: @"^(?<name>.+?)\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+shock\s+damage\s+per\s+second\s+to\s+Health\s+and\s+Magicka(?:,\s*with\s+a\s+chance\s+of\s+disintegrating\s+opponents)?\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex FrozenBetweenOblivionAndTamrielRegex = new(
        pattern: @"^Targets\s+are\s+frozen\s+between\s+Oblivion\s+and\s+Tamriel\s+for\s+(?<dur><\s*dur\s*>)\s+seconds(?:,)?\s+and\s+immune\s+to\s+all\s+damage\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ThunderShockDamageHalfToMagickaRegex = new(
        pattern: @"^A\s+Thunder\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+shock\s+damage\s+to\s+Health\s+and\s+half\s+that\s+to\s+Magicka\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex LightningBoltShockDamageHalfToMagickaRegex = new(
        pattern: @"^(?<kind>Lightning\s+bolt|A\s+bolt\s+of\s+lightning)\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+shock\s+damage\s+to\s+Health\s+and\s+half(?:\s+that)?\s+to\s+Magicka(?:,?\s*(?<leaps>then\s+leaps\s+to\s+a\s+new\s+target))?\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex TargetsTakeFrostDamageForSecondsPlusStaminaDamageRegex = new(
        pattern: @"^Targets\s+take\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+frost\s+damage\s+for\s+(?<dur><\s*dur\s*>)\s+seconds,\s+plus\s+Stamina\s+damage\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex RestoreAttrByPointsRegex = new(
        pattern: @"^Restore(?:s)?\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+(?<attr>Health|Magicka|Stamina)\.?\s*$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex BlastOfColdDamagePerSecondToHealthAndStaminaRegex = new(
        pattern: @"^A\s+blast\s+of\s+cold\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+per\s+second\s+to\s+Health\s+and\s+Stamina\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex StreamOfColdDamagePerSecondToHealthAndStaminaRegex = new(
        pattern: @"^A\s+stream\s+of\s+cold\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+per\s+second\s+to\s+Health\s+and\s+Stamina\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex RayOfFireDamagePerSecondExtraDamageRegex = new(
        pattern: @"^A\s+ray\s+of\s+fire\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+per\s+second\.\s*Targets\s+on\s+fire\s+take\s+extra\s+damage\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex LightningBeamShockDamageToHealthAndMagickaPerSecondRegex = new(
        pattern: @"^Lightning\s+beam\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+shock\s+damage\s+to\s+Health\s+and\s+Magicka\s+per\s+second\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex BurstOfSteamDamagePerSecondRegex = new(
        pattern: @"^A\s+burst\s+of\s+steam\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+per\s+second\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex TargetsTakeDamageForSecondsRegex = new(
        pattern: @"^Targets\s+take\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex BurnsEnemyForSecondsDamageToHealthEverySecondRegex = new(
        pattern: @"^An\s+attack\s+(?:which|that)\s+burns\s+the\s+enemy\s+for\s+(?<dur>[+-]?<\s*(?:dur|\d+)\s*>|\b[0-9]+(?:\.[0-9]+)?\b)\s+seconds(?:,)?\s+dealing\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+to\s+Health\s+(?:(?:every|each)\s+second|per\s+second)\.?\s*$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex LaceratesEnemyBleedsForSecondsDealingDamageToHealthAndStaminaEverySecondRegex = new(
        pattern: @"^An\s+attack\s+(?:which|that)\s+lacerates\s+the\s+enemy\s+causing\s+it\s+to\s+bleed\s+for\s+(?<dur>\b[0-9]+(?:\.[0-9]+)?\b)\s+seconds(?:,)?\s+dealing\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+to\s+Health\s+and\s+Stamina\s+(?:(?:every|each)\s+second|per\s+second)\.?\s*$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex SeismicWaveStaggersEnemiesDealingDamageRegex = new(
        pattern: @"^A\s+seismic\s+wave\s+that\s+staggers\s+enemies\s+in\s+front\s+of\s+you,\s+dealing\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex PileOfThrownRocksDamageAndStaggersRegex = new(
        pattern: @"^A\s+pile\s+of\s+thrown\s+rocks\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+and\s+staggers\s+your\s+foe\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex SpearOfStoneDamageAndStaggersRegex = new(
        pattern: @"^A\s+spear\s+of\s+stone\s+that\s+does\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+damage\s+and\s+staggers\s+your\s+foe\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex ShockingExplosionCenteredOnCasterRegex = new(
        pattern: @"^A\s+(?<mag><\s*mag\s*>)\s+point\s+Shocking\s+explosion\s+centered\s+on\s+the\s+caster\.\s*Does\s+more\s+damage\s+to\s+closer\s+targets\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex FieryBreathForMagDamagesRegex = new(
        pattern: @"^Releases\s+a\s+fiery\s+breath\s+for\s+(?<mag><\s*mag\s*>)\s+damages?\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DamagesCloseDwarvenTargetsByMagPointsRegex = new(
        pattern: @"^damages\s+close\s+Dwarven\s+targets\s+by\s+(?<mag><\s*mag\s*>)\s+points?\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex CausesPoisonDamageOnNonDwarvenTargetsForSecondsRegex = new(
        pattern: @"^Causes\s+(?<mag><\s*mag\s*>)\s+points?\s+of\s+poison\s+damage\s+on\s+non\s+dwarven\s+targets\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex SignedAmountForDurationRegex = new(
        pattern: @"^(?<mag>[+-]?<\s*mag\s*>)\s+(?<subject>[A-Za-z][A-Za-z \-']*?)\s+for\s+(?<dur><\s*dur\s*>)\s+seconds\.?$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Dictionary<string, string> KnownNamesKo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Golden Lightning"] = "황금 번개",
    };

    private static readonly Dictionary<string, string> KnownSubjectsKo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Health"] = "체력",
        ["Magicka"] = "매지카",
        ["Stamina"] = "지구력",
        ["Carry Weight"] = "무게 한계",
        ["Smithing"] = "제련",
        ["Enchanting"] = "마법부여",
        ["Two-Handed"] = "양손무기",
        ["One-Handed"] = "한손무기",
        ["Archery"] = "궁술",
        ["Light Armor"] = "경갑",
        ["Heavy Armor"] = "중갑",
        ["Sneak"] = "은신",
        ["Pickpocket"] = "소매치기",
        ["Lockpicking"] = "자물쇠 따기",
        ["Conjuration"] = "소환마법",
        ["Destruction"] = "파괴마법",
        ["Restoration"] = "회복마법",
        ["Illusion"] = "환영마법",
        ["Alteration"] = "변화마법",
        ["Speech"] = "화술",
        ["Alchemy"] = "연금술",
    };

    public static string Fix(string source, string dest, string targetLang)
    {
        if (!IsKorean(targetLang) && !LooksLikeKoreanText(dest))
        {
            return dest;
        }

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest))
        {
            return dest;
        }

        // Some Skyrim effects use numeric placeholders for duration ("for <150> seconds") instead of <dur>.
        // Fix common Korean misplacements even when <mag>/<dur> tags are not present.
        var numericTemplated = TryFixNumericDurationTemplates(source, dest);
        if (!ReferenceEquals(numericTemplated, dest))
        {
            return numericTemplated;
        }

        if (!MagRegex.IsMatch(dest) && !DurRegex.IsMatch(dest))
        {
            return dest;
        }

        dest = NormalizeDuplicateSigns(dest);

        // Prefer deterministic templates for known magic-effect style strings (more reliable than LLM word order).
        var templated = TryFixWithTemplates(source, dest);
        if (!ReferenceEquals(templated, dest))
        {
            return templated;
        }

        // Fallback: if the placeholders look swapped in Korean context, swap them (including +/- if present).
        if (LooksLikeSwappedInKorean(dest, out var magToken, out var durToken))
        {
            return SwapOnce(dest, magToken, durToken);
        }

        return dest;
    }

}
