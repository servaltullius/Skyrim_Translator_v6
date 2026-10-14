using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class TranslationBatchGroupingTests
{
    [Theory]
    [InlineData("//ES_debuff5_Strikefly1", "ES_debuff5_Strikefly")]
    [InlineData("// RimRing003", "RimRing")]
    [InlineData("ES_debuff5_Strikefly1", "ES_debuff5_Strikefly")]
    [InlineData("//", "")]
    public void EdidStem_IgnoresTheCommentSlashes(string edid, string expected)
        => Assert.Equal(expected, TranslationBatchGrouping.NormalizeEdidStem(edid));

    [Fact]
    public void SeriesNamesShareOneKey_AcrossRecordTypesAndEditorIds()
    {
        // Elden Rim: one "AttackDebuff - X" series under two EDID families, which sorted far apart.
        var keys = TranslationBatchGrouping.ComputeGroupKeys(new (string?, string?, string)[]
        {
            ("SPEL:FULL", "ES_AttackDebuff_Repulse-BuffOnly", "AttackDebuff - Knockback - BuffOnly"),
            ("SPEL:FULL", "RimImpact_Paralysis", "AttackDebuff - Paralysis"),
            ("MGEF:FULL", "RimImpact_Drag", "AttackDebuff - Drag"),
        });

        Assert.All(keys, k => Assert.Equal("AttackDebuff", k));
    }

    [Fact]
    public void RecordDescription_FollowsItsNameIntoTheSeries()
    {
        var keys = TranslationBatchGrouping.ComputeGroupKeys(new (string?, string?, string)[]
        {
            ("ARMO:FULL", "RimRing003", "Ring of Rim - Counter Attack"),
            ("ARMO:DESC", "RimRing003", "Allows you to perform Elden Counter while blocking."),
            ("SPEL:FULL", "RimSpell003", "Ring of Rim - Counter Attack"),
            ("PERK:FULL", "RimPerk005", "Ring of Rim - Evoker Crossbow"),
        });

        Assert.All(keys, k => Assert.Equal("Ring of Rim", k));
    }

    [Fact]
    public void OneSkillWrittenInSeveralForms_SharesOneKey()
    {
        // Elden Rim names a skill under several prefixes and qualifiers; grouped by prefix, the forms were translated
        // in different batches as 물새 난격 / 물새 난무 and 에오히드의 검무 / 에오히드의 춤추는 칼날.
        var keys = TranslationBatchGrouping.ComputeGroupKeys(new (string?, string?, string)[]
        {
            ("MGEF:FULL", "PlayerKnow_80", "Learning - Waterfowl Dance"),
            ("MGEF:DNAM", "PlayerKnow_80", "Slash foes in a flurry."),
            ("MGEF:FULL", "Learning_81", "Learning - Moonlight Thrust"),
            ("MGEF:FULL", "Learning_82", "Learning - Sky Shot"),
            ("MGEF:FULL", "EnchDisable_30", "Waterfowl Dance (1H Sword)"),
            ("ENCH:FULL", "WarAshEnchDisable_31", "Weapon Art - Waterfowl Dance (Charged)"),
        });

        Assert.Equal(new[] { "Waterfowl Dance", "Waterfowl Dance", "Learning", "Learning", "Waterfowl Dance", "Waterfowl Dance" }, keys);
    }

    [Fact]
    public void OneWordAfterAPrefix_IsNoSkillName()
    {
        // "Fire" after several prefixes is an element, not one skill: the series keys stay.
        var keys = TranslationBatchGrouping.ComputeGroupKeys(new (string?, string?, string)[]
        {
            ("SPEL:FULL", "Ench1", "Enchanting - Fire"),
            ("SPEL:FULL", "Ench2", "Enchanting - Frost"),
            ("SPEL:FULL", "Ench3", "Enchanting - Shock"),
            ("MGEF:FULL", "Oil1", "Blade Oil - Fire"),
        });

        Assert.Equal(new[] { "Enchanting", "Enchanting", "Enchanting", "Oil" }, keys);
    }

    [Fact]
    public void PrefixSharedByFewerThanThreeRows_KeepsTheEditorIdKey()
    {
        var keys = TranslationBatchGrouping.ComputeGroupKeys(new (string?, string?, string)[]
        {
            ("MGEF:FULL", "StanceH", "Trigger - High"),
            ("MGEF:FULL", "StanceL", "Trigger - Low"),
            ("MGEF:FULL", "BowShoot01", "Launching Bolt"),
        });

        Assert.Equal(new[] { "StanceH", "StanceL", "BowShoot" }, keys);
    }

    [Fact]
    public void DialogueLines_StayWithTheirTopic()
    {
        // "Yes (Persuade)" style choices share a prefix but belong to different conversations.
        var keys = TranslationBatchGrouping.ComputeGroupKeys(new (string?, string?, string)[]
        {
            ("INFO:NAM1", "DialogueSerana01", "Yes (Persuade) I'll help."),
            ("INFO:NAM1", "DialogueMaven02", "Yes (Persuade) Fine."),
            ("INFO:NAM1", "DialogueIngun03", "Yes (Persuade) Sure."),
        });

        Assert.Equal(new[] { "DialogueSerana", "DialogueMaven", "DialogueIngun" }, keys);
    }

    [Fact]
    public void SingleRowKey_MatchesTheOldRule()
        => Assert.Equal("RimRing", TranslationBatchGrouping.ComputeGroupKey("ARMO:FULL", "//RimRing003", "Ring of Rim - Counter Attack"));
}
