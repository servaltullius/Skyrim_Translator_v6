using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

// Text in a rune font ($DaedricFont and the like) is a cipher the game shows as runes, and the font has no Hangul: the
// Korean patch's Atronach Forge recipes, translated, showed boxes. Such text stays the source's everywhere.
public sealed class CipherFontSpansTests
{
    private const string Recipe = "<p align='center'><font face='$DaedricFont'><font size='31'>\n\n\n\nEbony Mace\n\nDaedra Heart\n\nBlack Soul Gem\n\nCenturion Dynamo\n</font></p>\n";
    private const string KoreanRecipe = "<p align='center'><font face='$DaedricFont'><font size='31'>\n\n\n\n에보니 철퇴\n\n데이드라 심장\n\n검은 소울젬\n\n백부장 동력장치\n</font></p>\n";

    [Fact]
    public void Find_RunsToTheClosingTagOrTheParagraphsEnd()
    {
        // The recipe's face font is never closed: its </font> closes the size font, and the paragraph ends it.
        var recipe = Assert.Single(CipherFontSpans.Find(Recipe));
        Assert.Equal("<font face='$DaedricFont'><font size='31'>\n\n\n\nEbony Mace\n\nDaedra Heart\n\nBlack Soul Gem\n\nCenturion Dynamo\n</font>",
            Recipe.Substring(recipe.Start, recipe.Length));

        // A Mythic Dawn commentary's title word, the font left open: the book's text after </p> is not runes.
        const string ghartok = "By\nMankar Camoran\n\n<font face='$DaedricFont'>GHARTOK</p>[pagebreak]<p align=\"left\">Text";
        var title = Assert.Single(CipherFontSpans.Find(ghartok));
        Assert.Equal("<font face='$DaedricFont'>GHARTOK", ghartok.Substring(title.Start, title.Length));

        const string chim = "<font face=\"$DragonFont\">CHIM</font> and <font face='$MageScriptFont'>ALTADOON</font>";
        Assert.Equal(new[] { "<font face=\"$DragonFont\">CHIM</font>", "<font face='$MageScriptFont'>ALTADOON</font>" },
            CipherFontSpans.Find(chim).Select(s => chim.Substring(s.Start, s.Length)));

        // A handwriting font is ordinary text.
        Assert.Empty(CipherFontSpans.Find("<font face='$HandwrittenFont'>Dear Brother,</font>"));
    }

    // Dwemer Inquiries: a runic poem, then another face for the book's text, the Dwemer font never closed. The body is
    // not runes, and the span ends where the face changes.
    [Fact]
    public void Find_EndsWhereAnotherFaceBegins()
    {
        const string dwemer = "<p align=\"left\">\r\n<font face='$DwemerFont'><font size='15'></font>\r\nIn the Deep Halls, far from Men\r\n"
            + "Dwarven Pride, Stronger than bone\r\n<font face='$SkyrimBooks'><font size='20'></font>\r\nThe limited written record supports";
        var span = Assert.Single(CipherFontSpans.Find(dwemer));
        Assert.Equal(CipherFontSpans.End.OtherFace, span.End);
        Assert.EndsWith("Stronger than bone\r\n", dwemer.Substring(span.Start, span.Length), StringComparison.Ordinal);
        Assert.Contains("The limited written record", CipherFontSpans.Remove(dwemer), StringComparison.Ordinal);
    }

    // The Falmer journals open a paragraph inside the font; it does not end the runes, the font's </font> does.
    [Fact]
    public void Find_GoesOnThroughAParagraphOpenedInside()
    {
        const string falmer = "[pagebreak]\r\n<font face='$FalmerFont'>\r\n<p align=\"left\">\r\nFOURTH EVENING STAR\r\n\r\nI USED TO DREAM\r\n</font>\r\n[pagebreak]";
        var span = Assert.Single(CipherFontSpans.Find(falmer));
        Assert.Equal(CipherFontSpans.End.ClosingTag, span.End);
        Assert.Equal("<font face='$FalmerFont'>\r\n<p align=\"left\">\r\nFOURTH EVENING STAR\r\n\r\nI USED TO DREAM\r\n</font>", falmer.Substring(span.Start, span.Length));
    }

    // A translation that left out the </font>, or moved it past its translated text, would take that text into the
    // span: restoring it lost the text. Such a translation is left for the integrity check to refuse, as it was.
    [Theory]
    [InlineData("<font face='$DaedricFont'>RUNE 새벽이 밝는다.")]
    [InlineData("<font face='$DaedricFont'>RUNE 새벽이 밝는다.</font>")]
    public void RestoreFromSource_LeavesASpanThatTookTheTextAfterIt(string translated)
    {
        const string source = "<font face='$DaedricFont'>RUNE</font> The dawn rises.";

        Assert.Same(translated, CipherFontSpans.RestoreFromSource(source, translated));
        Assert.NotNull(KnownTranslationFill.Prepare(1, source, translated, "korean", enableTemplateFixer: true).Failure);
    }

    // An Almsivi note writes a comma in a handwriting font between Daedric words: the runes go on after it closes.
    [Fact]
    public void Find_GoesOnAfterAnotherFaceInsideCloses()
    {
        const string note = "<font face='$DaedricFont'>You have lost the temple<font face='$HandwrittenFont'>, </font> but you are not lost</font> Signed";
        Assert.Equal(new[] { "<font face='$DaedricFont'>You have lost the temple", " but you are not lost</font>" },
            CipherFontSpans.Find(note).Select(s => note.Substring(s.Start, s.Length)));
        Assert.Equal("<font face='$HandwrittenFont'>, </font> Signed", CipherFontSpans.Remove(note));

        // A self-closing face sets nothing, so it does not end the runes.
        const string selfClosing = "<font face='$DaedricFont'>RUNE<font face='$HandwrittenFont'/>MORE</font>";
        Assert.Equal(selfClosing, selfClosing.Substring(Assert.Single(CipherFontSpans.Find(selfClosing)).Start));
    }

    // A comma the patch changed in the runes' English is a warning, not the error of Hangul the font cannot show.
    [Fact]
    public async Task QualityCheck_WarnsOfPunctuationInTheRunes()
    {
        const string source = "<font face='$FalmerFont'>\nLOOSE-TONGUED SOURCE\n</font>";
        var issues = await LqaScanner.ScanAsync(new[]
        {
            new LqaScanEntry(1, 1, "Gallus", "BOOK:DESC", StringEntryStatus.Done, source, source.Replace("-", " ")),
        }, "korean", Array.Empty<GlossaryEntry>());

        var issue = Assert.Single(issues, i => i.Code == "cipher_font_text");
        Assert.Equal("Warn", issue.Severity);
        Assert.Contains("문장부호", issue.Message, StringComparison.Ordinal);
    }

    // The rune span is numbered with the other protected pieces in the order they stand, so the check still sees a
    // translation that moved it.
    [Fact]
    public void Masker_NumbersTheSpanInTextOrder()
    {
        const string source = "Title\n<font face='$DaedricFont'>RUNE</font>\nBody";
        var masked = new PlaceholderMasker().Mask(source);

        Assert.Equal("Title__XT_PH_0000____XT_PH_0001____XT_PH_0002__Body", masked.Text);
        Assert.Throws<InvalidOperationException>(() =>
            TokenValidator.ValidateFinalTextIntegrity(source, "<font face='$DaedricFont'>RUNE</font>\n제목\n본문", "test"));
    }

    // A row of nothing but runes has nothing to translate: it keeps its source without a request.
    [Fact]
    public void ARowOfOnlyRunes_HasNothingToTranslate()
    {
        Assert.False(TranslationService.HasLettersToTranslate(Recipe));
        Assert.True(TranslationService.HasLettersToTranslate("By Mankar Camoran <font face='$DaedricFont'>GHARTOK</p>"));
    }

    [Fact]
    public void Masker_ProtectsTheSpanAsOnePiece_AndGivesItBack()
    {
        var masker = new PlaceholderMasker();
        const string text = "<font face='$DaedricFont'>GHARTOK</p>\nThe <b>dawn</b> rises.";

        var masked = masker.Mask(text);

        Assert.DoesNotContain("GHARTOK", masked.Text, StringComparison.Ordinal);
        Assert.Contains("The ", masked.Text, StringComparison.Ordinal);
        Assert.Contains("<font face='$DaedricFont'>GHARTOK", masked.TokenToOriginal.Values);
        Assert.Contains("<b>", masked.TokenToOriginal.Values);
        Assert.Equal(text, masker.Unmask(masked.Text, masked.TokenToOriginal));

        // The recipe is nothing but protected pieces: nothing in it for the model to translate.
        var recipe = masker.Mask(Recipe);
        Assert.Matches(@"^(__XT_PH_\d{4}__)+$", recipe.Text);
    }

    [Fact]
    public void RestoreFromSource_GivesEachSpanBackTheSources()
    {
        Assert.Equal(Recipe.TrimEnd('\n') + "\n", CipherFontSpans.RestoreFromSource(Recipe, KoreanRecipe));

        // Another number of spans is left for the integrity check to refuse.
        const string noSpan = "에보니 철퇴, 데이드라 심장";
        Assert.Same(noSpan, CipherFontSpans.RestoreFromSource(Recipe, noSpan));
    }

    [Fact]
    public void KnownTranslationFill_TakesTheRecipeBackFromTheSource()
    {
        var prepared = KnownTranslationFill.Prepare(1, Recipe, KoreanRecipe, "korean", enableTemplateFixer: true);

        Assert.Null(prepared.Failure);
        Assert.Equal(Recipe, prepared.Text);
        Assert.Throws<InvalidOperationException>(() => TokenValidator.ValidateFinalTextIntegrity(Recipe, KoreanRecipe, "test"));
    }

    // A project translated before holds Korean there: its export writes the source's text instead of refusing the row.
    [Fact]
    public void Export_DoesNotRefuseARecipeTranslatedBefore()
    {
        Assert.Empty(XTranslatorAi.Core.Plugins.PluginWriter.FindTranslationProblems(Recipe, KoreanRecipe, "test", allowDroppedInitialsAndPronouns: false));
        Assert.NotEmpty(XTranslatorAi.Core.Plugins.PluginWriter.FindTranslationProblems(Recipe, "에보니 철퇴", "test", allowDroppedInitialsAndPronouns: false));
    }

    [Fact]
    public async Task Export_WritesTheSourcesTextInTheRuneFont()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xt-cipher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string english = "Notes: <font face='$DaedricFont'>GHARTOK</font>";
            var input = Path.Combine(dir, "Test.esp");
            await File.WriteAllBytesAsync(input, PluginProjectIntegrationTests.CreateMinimalPlugin(english));
            var document = await XTranslatorAi.Core.Plugins.PluginReader.ReadAsync(input, new(), default);
            var field = Assert.Single(document.Fields, f => f.SourceText == english);

            var result = await XTranslatorAi.Core.Plugins.PluginWriter.ExportAsync(document,
                new Dictionary<string, string> { [field.Key] = "기록: <font face='$DaedricFont'>가르톡</font>" }, new(Path.Combine(dir, "out")), default);

            var reread = await XTranslatorAi.Core.Plugins.PluginReader.ReadAsync(result.PluginPath, new(), default);
            Assert.Equal("기록: <font face='$DaedricFont'>GHARTOK</font>", Assert.Single(reread.Fields, f => f.Key == field.Key).SourceText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PostEditReapply_TakesTheRecipeBackFromTheSource()
        => Assert.Equal(Recipe, TranslationPostEdits.Apply("korean", Recipe, KoreanRecipe, enableTemplateFixer: true));

    [Fact]
    public async Task QualityCheck_NamesTheRuneFont_AndDoesNotCallTheCipherUntranslated()
    {
        var entries = new[]
        {
            new LqaScanEntry(1, 1, "AtrFrgDaedricRecipe11", "BOOK:DESC", StringEntryStatus.Done, Recipe, KoreanRecipe),
            new LqaScanEntry(2, 2, "AtrFrgDaedricRecipe10", "BOOK:DESC", StringEntryStatus.Done, Recipe.Replace("Mace", "Dagger"), Recipe.Replace("Mace", "Dagger")),
        };

        var issues = await LqaScanner.ScanAsync(entries, "korean", Array.Empty<GlossaryEntry>());

        var cipher = Assert.Single(issues, i => i.Id == 1);
        Assert.Equal("cipher_font_text", cipher.Code);
        Assert.Contains("룬 문자 글꼴", cipher.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(issues, i => i.Id == 2);
    }
}
