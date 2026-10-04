using System.Linq;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// JSON glossaries written by tools such as Python's json.dump escape every Hangul syllable as \uXXXX.
/// The parser kept only the letter after the backslash, so "\uB4DC\uB798\uACE4" was imported as "uB4DCuB798uACE4".
/// </summary>
public class GlossaryFileParserTests
{
    [Fact]
    public void CategorizedJson_DecodesUnicodeEscapes()
    {
        var text = "{\n  \"Creatures\": {\n    \"Dragon\": \"\\uB4DC\\uB798\\uACE4\"\n  }\n}";

        var entry = Assert.Single(GlossaryFileParser.ParseEntries(text));

        Assert.Equal("Creatures", entry.Category);
        Assert.Equal("Dragon", entry.Source);
        Assert.Equal("드래곤", entry.Target);
    }

    [Fact]
    public void FlatJson_DecodesUnicodeEscapes()
    {
        var text = "{\"Dragon\": \"\\uB4DC\\uB798\\uACE4\", \"Say \\\"hi\\\"\": \"\\uC548\\uB155\"}";

        var entries = GlossaryFileParser.ParseEntries(text);

        Assert.Contains(entries, e => e.Source == "Dragon" && e.Target == "드래곤");
        Assert.Contains(entries, e => e.Source == "Say \"hi\"" && e.Target == "안녕");
    }

    [Fact]
    public void SurrogatePairEscape_DecodesToOneCharacter()
    {
        var text = "{\"Star\": \"\\uD83C\\uDF1F \\uBCC4\"}";

        var entry = Assert.Single(GlossaryFileParser.ParseEntries(text));

        Assert.Equal("🌟 별", entry.Target);
    }

    [Fact]
    public void MalformedUnicodeEscape_IsKeptAsWritten()
    {
        var text = "{\"Dragon\": \"\\uZZ \\uB4DC\"}";

        var entry = Assert.Single(GlossaryFileParser.ParseEntries(text));

        Assert.Equal("\\uZZ 드", entry.Target);
    }

    [Fact]
    public void PlainEntries_AreUnchanged()
    {
        var entries = GlossaryFileParser.ParseEntries("{\"Whiterun\": \"화이트런\", \"Riften\": \"리프튼\"}");

        Assert.Equal(new[] { "화이트런", "리프튼" }, entries.Select(e => e.Target).ToArray());
    }
}
