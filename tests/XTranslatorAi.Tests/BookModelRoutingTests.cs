using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

public sealed class BookModelRoutingTests
{
    [Theory]
    [InlineData("BOOK:DESC", false, false, true, true)]
    [InlineData(" book : desc ", false, false, true, true)]
    [InlineData("BOOK:DESC", false, true, false, false)]
    [InlineData("BOOK:FULL", true, false, true, false)]
    [InlineData("BOOK:FULL", true, true, false, true)]
    [InlineData("BOOK:DESC", false, true, true, true)]
    [InlineData("BOOK:FULL", true, true, true, true)]
    [InlineData("BOOK:DESC", false, false, false, false)]
    [InlineData("MESG:DESC", false, true, true, false)]
    [InlineData("BOOK:DESC:OTHER", false, true, true, false)]
    [InlineData(null, false, true, true, false)]
    public void TitleAndBodySelections_AreIndependent(string? rec, bool title, bool titlesEnabled, bool bodiesEnabled, bool expected)
        => Assert.Equal(expected, BookModelRouting.ShouldOverride(rec, title, titlesEnabled, bodiesEnabled));

    [Fact]
    public void LegacyTitleSetting_RemainsTitleOnlyAndBodySelectionRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {"enableBookFullModelOverride":true,"bookFullModel":"gemini-3.8-flash",
                 "enablePromptCache":false,"enableQualityEscalation":true}
                """);
            var store = new AppSettingsStore(path);
            var legacy = store.Load();
            Assert.True(legacy.EnableBookFullModelOverride);
            Assert.False(legacy.EnableBookBodyModelOverride);
            Assert.Equal("gemini-3.8-flash", legacy.BookFullModel);
            store.Save(legacy with { EnableBookFullModelOverride = false, EnableBookBodyModelOverride = true });
            var saved = store.Load();
            Assert.False(saved.EnableBookFullModelOverride);
            Assert.True(saved.EnableBookBodyModelOverride);
            Assert.Equal(legacy.BookFullModel, saved.BookFullModel);
            Assert.False(saved.EnablePromptCache);
            Assert.True(saved.EnableQualityEscalation);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
