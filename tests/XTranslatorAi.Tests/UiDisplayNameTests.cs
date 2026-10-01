using System.Linq;
using XTranslatorAi.App.Converters;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class UiDisplayNameTests
{
    [Fact]
    public void EveryStatus_HasAKoreanLabel_ThatParsesBack()
    {
        foreach (var status in System.Enum.GetValues<StringEntryStatus>())
        {
            var label = StringEntryStatusLabels.ToLabel(status);
            Assert.NotEqual(status.ToString(), label);
            Assert.True(StringEntryStatusLabels.TryParse(label, out var parsed));
            Assert.Equal(status, parsed);
        }
    }

    [Theory]
    [InlineData("Done", StringEntryStatus.Done)]
    [InlineData("error", StringEntryStatus.Error)]
    public void EnumNames_StillParse_ForOlderSavedFilters(string text, StringEntryStatus expected)
    {
        Assert.True(StringEntryStatusLabels.TryParse(text, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("(전체)")]
    [InlineData("42")]
    [InlineData("")]
    public void NonStatusText_DoesNotParse(string text) => Assert.False(StringEntryStatusLabels.TryParse(text, out _));

    [Fact]
    public void FilterLabels_CoverEveryStatusOnce()
        => Assert.Equal(System.Enum.GetValues<StringEntryStatus>().Length, StringEntryStatusLabels.FilterLabels().Distinct().Count());

    [Fact]
    public void GlossaryModes_HaveKoreanNames()
    {
        foreach (var mode in System.Enum.GetValues<GlossaryMatchMode>())
            Assert.NotEqual(mode.ToString(), EnumDisplayConverter.ToDisplay(mode));
        foreach (var mode in System.Enum.GetValues<GlossaryForceMode>())
            Assert.NotEqual(mode.ToString(), EnumDisplayConverter.ToDisplay(mode));
    }
}
