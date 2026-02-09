using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class PlaceholderSemanticHintInjectorTests
{
    [Fact]
    public void Inject_ForKorean_AddsHintMarkersNextToSemanticPlaceholderTokens()
    {
        var text = "__XT_PH_MAG_0000__ __XT_PH_DUR_0001__ __XT_PH_NUM_0002__";
        var injected = PlaceholderSemanticHintInjector.Inject("korean", text);

        Assert.Contains("__XT_PH_MAG_0000__⟦XT_MAG=100⟧", injected, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_DUR_0001__⟦XT_DUR=5⟧", injected, StringComparison.Ordinal);
        Assert.Contains("__XT_PH_NUM_0002__⟦XT_NUM=10⟧", injected, StringComparison.Ordinal);
    }

    [Fact]
    public void Strip_RemovesHintMarkers()
    {
        var injected = "__XT_PH_MAG_0000__⟦XT_MAG=100⟧ __XT_PH_DUR_0001__⟦XT_DUR=5⟧";
        var stripped = PlaceholderSemanticHintInjector.Strip(injected);
        Assert.Equal("__XT_PH_MAG_0000__ __XT_PH_DUR_0001__", stripped);
    }

    [Fact]
    public void Inject_ForNonKorean_DoesNotAddMarkers()
    {
        var text = "__XT_PH_MAG_0000__ __XT_PH_DUR_0001__";
        var injected = PlaceholderSemanticHintInjector.Inject("english", text);
        Assert.Equal(text, injected);
    }
}

