using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Tests;

public sealed class PluginFieldRegistryTests
{
    [Theory]
    [InlineData(4, "EPF2", PluginStringTableKind.Strings)]
    [InlineData(7, "EPFD", PluginStringTableKind.Strings)]
    public void PerkDisplayParameters_UseLocalizedStrings(
        byte parameterType, string field, PluginStringTableKind? expectedTable)
    {
        var rule = PluginFieldRegistry.GetRule("PERK", field, null,
            new[] { Sub("PRKE", 2, 0, 0), Sub("EPFT", parameterType) });

        Assert.NotNull(rule);
        Assert.Equal(expectedTable, rule.LocalizedTable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void PerkNonDisplayParameters_AreNeverExposedForTranslation(byte parameterType)
    {
        var preceding = new[] { Sub("PRKE", 2, 0, 0), Sub("EPFT", parameterType) };
        Assert.Null(PluginFieldRegistry.GetRule("PERK", "EPFD", null, preceding));
    }

    [Fact]
    public void PerkSelectText_GraphVariableIdentifierIsNotPlayerVisibleText()
    {
        // BlockRunner's EPFD contains bPerkShieldCharge. Mutagen identifies
        // DATA entry point 54 as SetBooleanGraphVariable, function 11 as
        // SelectText, and EPFT 6 as a non-translated string parameter.
        var preceding = new[]
        {
            Sub("PRKE", 2, 0, 0), Sub("DATA", 54, 11, 1), Sub("EPFT", 6),
        };
        Assert.Null(PluginFieldRegistry.GetRule("PERK", "EPFD", "BlockRunner", preceding));

        // A new identifier effect must not inherit an earlier display-string rule.
        var earlierDisplay = new[] { Sub("PRKE", 2, 0, 0), Sub("EPFT", 7), Sub("PRKF") };
        Assert.Null(PluginFieldRegistry.GetRule("PERK", "EPFD", "BlockRunner",
            earlierDisplay.Concat(preceding).ToArray()));
    }

    [Fact]
    public void PerkButtonLabel_RequiresTheSpellWithStringsParameterType()
    {
        Assert.Null(PluginFieldRegistry.GetRule("PERK", "EPF2", null,
            new[] { Sub("PRKE", 2, 0, 0), Sub("EPFT", 7) }));
    }

    [Fact]
    public void PerkParameterType_DoesNotEscapeAnEffectOrAnUnfinishedNewEffect()
    {
        var open = new[] { Sub("PRKE", 2, 0, 0), Sub("EPFT", 7) };
        Assert.Null(Rule(open.Append(Sub("PRKF")).ToArray()));
        Assert.Null(Rule(open.Append(Sub("PRKF")).Append(Sub("PRKE", 2, 0, 0)).ToArray()));
        // Even a missing old PRKF must not let the previous effect supply the new EPFT.
        Assert.Null(Rule(open.Append(Sub("PRKE", 2, 0, 0)).ToArray()));
        Assert.Null(Rule(new[] { Sub("EPFT", 7) }));

        static PluginFieldRule? Rule(PluginSubrecordDescriptor[] preceding)
            => PluginFieldRegistry.GetRule("PERK", "EPFD", null, preceding);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(2, 0)]
    [InlineData(2, 2)]
    [InlineData(2, 4)]
    public void PerkTextParameter_RequiresAValidEntryPointEffect(byte effectType, int headerLength)
    {
        var header = new byte[headerLength];
        if (header.Length > 0) header[0] = effectType;
        Assert.Null(PluginFieldRegistry.GetRule("PERK", "EPFD", null,
            new[] { new PluginSubrecordDescriptor("PRKE", header), Sub("EPFT", 7) }));
    }

    [Fact]
    public void PerkMalformedOrReplacedParameterType_DoesNotReuseAnEarlierTextType()
    {
        var open = new[] { Sub("PRKE", 2, 0, 0), Sub("EPFT", 7) };
        foreach (var latest in new[] { Sub("EPFT"), Sub("EPFT", 7, 0), Sub("EPFT", 1) })
            Assert.Null(PluginFieldRegistry.GetRule("PERK", "EPFD", null, open.Append(latest).ToArray()));
    }

    [Fact]
    public void QuestObjectiveText_RemainsNormalStringsAcrossTargetAndConditionFields()
    {
        var preceding = new[]
        {
            Sub("INDX", 10, 0, 0, 0), Sub("QSDT", 0), Sub("CNAM", 1, 0, 0, 0),
            Sub("QOBJ", 10, 0), Sub("FNAM", 0, 0, 0, 0), Sub("QSTA"), Sub("CTDA"),
        };
        var rule = PluginFieldRegistry.GetRule("QUST", "NNAM", null, preceding);
        Assert.NotNull(rule);
        Assert.Equal(PluginStringTableKind.Strings, rule.LocalizedTable);
    }

    [Theory]
    [InlineData("ANAM")]
    [InlineData("ALST")]
    [InlineData("ALLS")]
    [InlineData("ALED")]
    [InlineData("INDX")]
    public void QuestRootOrAliasDescription_IsNotMistakenForObjectiveText(string boundary)
    {
        Assert.Null(PluginFieldRegistry.GetRule("QUST", "NNAM", null,
            new[] { Sub("QOBJ", 10, 0), Sub("NNAM", 1, 0, 0, 0), Sub(boundary) }));
        Assert.Null(PluginFieldRegistry.GetRule("QUST", "NNAM", null, Array.Empty<PluginSubrecordDescriptor>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("iCount")]
    [InlineData("fDistance")]
    [InlineData("bEnabled")]
    [InlineData("SUppercaseIsNotTheStringPrefix")]
    public void NumericOrUnknownGameSetting_DoesNotExposeItsDataAsText(string? edid)
    {
        Assert.Null(PluginFieldRegistry.GetRule("GMST", "DATA", edid, Array.Empty<PluginSubrecordDescriptor>()));
    }

    [Fact]
    public void StringGameSetting_UsesNormalStrings()
    {
        var rule = PluginFieldRegistry.GetRule("GMST", "DATA", "sAccept", Array.Empty<PluginSubrecordDescriptor>());
        Assert.NotNull(rule);
        Assert.Equal(PluginStringTableKind.Strings, rule.LocalizedTable);
    }

    [Theory]
    [InlineData("LSCR", "DESC", PluginStringTableKind.Strings)]
    [InlineData("BOOK", "DESC", PluginStringTableKind.DlStrings)]
    [InlineData("QUST", "CNAM", PluginStringTableKind.DlStrings)]
    [InlineData("INFO", "NAM1", PluginStringTableKind.IlStrings)]
    [InlineData("INFO", "RNAM", PluginStringTableKind.Strings)]
    public void SimilarDisplayFields_UseTheirOwnStringTable(string record, string field, PluginStringTableKind table)
    {
        var rule = PluginFieldRegistry.GetRule(record, field, null, Array.Empty<PluginSubrecordDescriptor>());
        Assert.NotNull(rule);
        Assert.Equal(table, rule.LocalizedTable);
    }

    [Theory]
    [InlineData("HDPT", "NAM1")]
    [InlineData("TES4", "CNAM")]
    [InlineData("TES4", "SNAM")]
    [InlineData("TES4", "MAST")]
    [InlineData("QUST", "FLTR")]
    [InlineData("BOOK", "EDID")]
    [InlineData("SNDR", "FNAM")]
    [InlineData("NPC_", "DNAM")]
    [InlineData("ARMO", "DNAM")]
    [InlineData("BOOK", "DATA")]
    [InlineData("INFO", "CNAM")]
    [InlineData("ZZZZ", "FULL")]
    [InlineData("ZZZZ", "DESC")]
    public void AssetPathsMetadataNumbersAndUnknownRecords_AreNotTranslated(string record, string field)
    {
        Assert.Null(PluginFieldRegistry.GetRule(record, field, null, Array.Empty<PluginSubrecordDescriptor>()));
    }

    private static PluginSubrecordDescriptor Sub(string type, params byte[] bytes) => new(type, bytes);
}
