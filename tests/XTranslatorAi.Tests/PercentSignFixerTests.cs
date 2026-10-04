using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Tests;

public sealed class PercentSignFixerTests
{
    [Fact]
    public void FixDuplicatePercents_CollapsesRepeatedPercents()
    {
        Assert.Equal("50% 확률", PercentSignFixer.FixDuplicatePercents("50%% 확률"));
        Assert.Equal("<mag>%", PercentSignFixer.FixDuplicatePercents("<mag>%%"));
        Assert.Equal("A%0f", PercentSignFixer.FixDuplicatePercents("A%%0f"));
        Assert.Equal("A%0f", PercentSignFixer.FixDuplicatePercents("A% %0f"));
    }

    // The stray-percent rule cut "%PLAYERNAME%." to "%PLAYERNAME." and the duplicate rule turned "%d%%" into
    // "%d%", so the final check failed those rows on every retry.
    [Theory]
    [InlineData("안녕 %PLAYERNAME%.", "Hello %PLAYERNAME%.")]
    [InlineData("%d%% 확률로 발동합니다.", "%d%% chance to trigger.")]
    [InlineData("%s의 %d%% 확률", "%d%% chance of %s")]
    public void FixDuplicatePercents_KeepsTheSourcesProtectedPercentText(string translated, string source)
    {
        Assert.Equal(translated, PercentSignFixer.FixDuplicatePercents(translated, source));
        Assert.False(LqaScanner.HasTokenMismatch(source, PercentSignFixer.FixDuplicatePercents(translated, source)));
    }

    [Fact]
    public void FixDuplicatePercents_KeepsNamedVariablesWithoutTheSource()
        => Assert.Equal("안녕 %PLAYERNAME%.", PercentSignFixer.FixDuplicatePercents("안녕 %PLAYERNAME%."));

    [Fact]
    public void FixDuplicatePercents_DoesNotChangeSinglePercent()
    {
        Assert.Equal("50% 확률", PercentSignFixer.FixDuplicatePercents("50% 확률"));
        Assert.Equal("<100%>", PercentSignFixer.FixDuplicatePercents("<100%>"));
    }

    [Fact]
    public void FixDuplicatePercents_RemovesStrayPercent_AfterPercentPlaceholder()
    {
        Assert.Equal("<25%>", PercentSignFixer.FixDuplicatePercents("<25%>%"));
        Assert.Equal("<25%>", PercentSignFixer.FixDuplicatePercents("<25%> %"));
    }

    [Fact]
    public void FixDuplicatePercents_RemovesStrayPercent_AfterPercentPlaceholder_WithInvisibleSeparators()
    {
        Assert.Equal("<25%>", PercentSignFixer.FixDuplicatePercents("<25%>\u200B%"));
        Assert.Equal("<25%>", PercentSignFixer.FixDuplicatePercents("<25%>\u2060%"));
        Assert.Equal("<25%>", PercentSignFixer.FixDuplicatePercents("<25%>\uFEFF%"));
    }

    [Fact]
    public void FixDuplicatePercents_RemovesStrayPercent_AfterWord()
    {
        Assert.Equal("밀어치기 증가합니다.", PercentSignFixer.FixDuplicatePercents("밀어치기% 증가합니다."));
        Assert.Equal("Shield bash damage 증가", PercentSignFixer.FixDuplicatePercents("Shield bash damage% 증가"));
    }

    [Fact]
    public void FixDuplicatePercents_RemovesPercentPointGarbage()
    {
        Assert.Equal("10%의 추가 방어 보호", PercentSignFixer.FixDuplicatePercents("10%포인트의 추가 방어 보호"));
        Assert.Equal("10% 증가", PercentSignFixer.FixDuplicatePercents("10% 포인트 증가"));
    }
}
