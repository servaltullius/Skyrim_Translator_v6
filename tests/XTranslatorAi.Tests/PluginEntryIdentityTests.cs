using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.Tests;

public sealed class PluginEntryIdentityTests
{
    [Fact]
    public void SharedStringSelector_SelectsOnlyExactTableAndIdDespiteIdenticalText()
    {
        var first = Row(0, PluginStringTableKind.Strings, 12);
        var shared = Row(1, PluginStringTableKind.Strings, 12);
        var otherId = Row(2, PluginStringTableKind.Strings, 123);
        var otherTable = Row(3, PluginStringTableKind.DlStrings, 12);
        var found = new[] { first, shared, otherId, otherTable }
            .Where(row => row.TryMatchLocationQuery("string:STRINGS/12", out var matches) && matches).ToArray();
        Assert.Equal(new[] { first, shared }, found);
    }

    [Fact]
    public void FormSelector_DistinguishesEdidlessInfoRowsAndShowsTheirLocation()
    {
        var first = Row(0, PluginStringTableKind.IlStrings, 1);
        var second = Row(1, PluginStringTableKind.IlStrings, 2);
        Assert.Null(first.Edid);
        Assert.True(first.TryMatchLocationQuery("form:01000800", out var matches));
        Assert.True(matches);
        Assert.True(second.TryMatchLocationQuery("form:01000800", out matches));
        Assert.False(matches);
        Assert.Contains("01000800", first.PluginLocationText);
        Assert.Contains("ILSTRINGS StringID 1", first.RowToolTip);
    }

    [Theory]
    [InlineData("string:STRINGS/12", false)]
    [InlineData("form:01000800", false)]
    [InlineData("row:12", true)]
    [InlineData("row:1", false)]
    public void XmlRows_DoNotPretendToHavePluginIds(string query, bool expected)
    {
        var row = new StringEntryViewModel(1, 12) { SourceText = "01000800 STRINGS/12" };
        Assert.True(row.TryMatchLocationQuery(query, out var matches));
        Assert.Equal(expected, matches);
        Assert.Equal("", row.PluginLocationText);
    }

    private static StringEntryViewModel Row(int order, PluginStringTableKind kind, uint id)
        => new(order + 1, order)
        {
            SourceText = "Same text", DestText = "Same text",
            PluginLocation = new($"INFO/{0x1000800 + order:X8}/0/NAM1/0", order, "INFO", "NAM1",
                (uint)(0x1000800 + order), null, order + 1, 0, "Same text", kind, id),
        };
}
