using System.Collections.Generic;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class GlossarySemanticHintInjectorTests
{
    [Fact]
    public void Inject_ForKorean_AddsHintMarkersNextToTermTokens()
    {
        var text = "__XT_TERM_G123_0000__ __XT_TERM_SESS_0001__";
        var tokenToReplacement = new Dictionary<string, string>
        {
            ["__XT_TERM_G123_0000__"] = "산적",
            ["__XT_TERM_SESS_0001__"] = "템테이션 하우스",
        };

        var injected = GlossarySemanticHintInjector.Inject("korean", text, tokenToReplacement);

        Assert.Contains("__XT_TERM_G123_0000__⟦XT_TERM=산적⟧", injected, StringComparison.Ordinal);
        Assert.Contains("__XT_TERM_SESS_0001__⟦XT_TERM=템테이션 하우스⟧", injected, StringComparison.Ordinal);
    }

    [Fact]
    public void Strip_RemovesHintMarkers()
    {
        var injected = "__XT_TERM_G123_0000__⟦XT_TERM=산적⟧ __XT_TERM_SESS_0001__⟦XT_TERM=템테이션 하우스⟧";
        var stripped = GlossarySemanticHintInjector.Strip(injected);
        Assert.Equal("__XT_TERM_G123_0000__ __XT_TERM_SESS_0001__", stripped);
    }

    [Fact]
    public void Inject_ForNonKorean_DoesNotAddMarkers()
    {
        var text = "__XT_TERM_G123_0000__";
        var tokenToReplacement = new Dictionary<string, string> { ["__XT_TERM_G123_0000__"] = "산적" };
        var injected = GlossarySemanticHintInjector.Inject("english", text, tokenToReplacement);
        Assert.Equal(text, injected);
    }
}

