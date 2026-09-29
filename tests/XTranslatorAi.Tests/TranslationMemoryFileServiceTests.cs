using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

public sealed class TranslationMemoryFileServiceTests
{
    [Fact]
    public void VersionedTsv_RoundTripsAllCharactersAndWhitespace()
    {
        var entries = new[]
        {
            (SourceText: "  A\tB\r\nC\rD\nE ", DestText: " 번역\t줄\r\n\"따옴표\"\\n "),
            (SourceText: "literal \\n and \\t", DestText: "원문 <Alias=Player> {0}"),
        };
        var serialized = TranslationMemoryFileService.BuildTsv(entries);
        var parsed = TranslationMemoryFileService.ParseTsvPairs(serialized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
        Assert.Equal(entries, parsed);
    }

    [Fact]
    public void LegacyTsv_BackslashSequencesAreNotReinterpreted()
    {
        var parsed = TranslationMemoryFileService.ParseTsvPairs(new[] { "Source\tTarget", "path\\name\t번역\\text", "  source \t target  " });
        Assert.Equal("path\\name", parsed[0].SourceText);
        Assert.Equal("번역\\text", parsed[0].DestText);
        Assert.Equal(("  source ", " target  "), parsed[1]);
    }

    [Fact]
    public void BrokenVersionedTsv_ThrowsInsteadOfSilentlyCorruptingText()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => TranslationMemoryFileService.ParseTsvPairs(new[]
        {
            "Source\tTarget\tXTranslatorAi-JSON-v1", "not-json\t\"target\"",
        }));
    }
}
