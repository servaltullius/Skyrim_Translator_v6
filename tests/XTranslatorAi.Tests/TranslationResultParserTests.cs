using System;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class TranslationResultParserTests
{
    [Fact]
    public void ParseTranslations_ValidJson_ReturnsParsedMap()
    {
        var json = """
            {
                "translations": [
                    { "id": 1, "text": "안녕하세요" },
                    { "id": 2, "text": "감사합니다" }
                ]
            }
            """;

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("안녕하세요", result[1]);
        Assert.Equal("감사합니다", result[2]);
    }

    [Fact]
    public void ParseTranslations_WithCodeFence_StripsAndParses()
    {
        var json = """
            ```json
            {
                "translations": [
                    { "id": 10, "text": "드래곤본" }
                ]
            }
            ```
            """;

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Single(result);
        Assert.Equal("드래곤본", result[10]);
    }

    [Fact]
    public void ParseTranslations_WithCodeFenceNoLanguageTag_StripsAndParses()
    {
        var json = """
            ```
            {
                "translations": [
                    { "id": 5, "text": "스카이림" }
                ]
            }
            ```
            """;

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Single(result);
        Assert.Equal("스카이림", result[5]);
    }

    [Fact]
    public void ParseTranslations_IncompleteJsonWithExtraText_ExtractsAndParses()
    {
        var text = """
            Here is the translation:
            {"translations": [{"id": 42, "text": "마법"}]}
            Hope this helps!
            """;

        var result = TranslationResultParser.ParseTranslations(text);

        Assert.Single(result);
        Assert.Equal("마법", result[42]);
    }

    [Fact]
    public void ParseTranslations_MissingTranslationsKey_ThrowsInvalidOperationException()
    {
        var json = """{"data": [{"id": 1, "text": "test"}]}""";

        Assert.Throws<InvalidOperationException>(() => TranslationResultParser.ParseTranslations(json));
    }

    [Fact]
    public void ParseTranslations_TranslationsNotArray_ThrowsInvalidOperationException()
    {
        var json = """{"translations": "not an array"}""";

        Assert.Throws<InvalidOperationException>(() => TranslationResultParser.ParseTranslations(json));
    }

    [Fact]
    public void ParseTranslations_EmptyInput_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => TranslationResultParser.ParseTranslations(""));
    }

    [Fact]
    public void ParseTranslations_WhitespaceInput_ThrowsException()
    {
        Assert.ThrowsAny<Exception>(() => TranslationResultParser.ParseTranslations("   "));
    }

    [Fact]
    public void ParseTranslations_EmptyTranslationsArray_ReturnsEmptyMap()
    {
        var json = """{"translations": []}""";

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseTranslations_ItemMissingId_SkipsItem()
    {
        var json = """
            {
                "translations": [
                    { "text": "no id item" },
                    { "id": 1, "text": "valid item" }
                ]
            }
            """;

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Single(result);
        Assert.Equal("valid item", result[1]);
    }

    [Fact]
    public void ParseTranslations_ItemMissingText_SkipsItem()
    {
        var json = """
            {
                "translations": [
                    { "id": 1 },
                    { "id": 2, "text": "has text" }
                ]
            }
            """;

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Single(result);
        Assert.Equal("has text", result[2]);
    }

    [Fact]
    public void ParseTranslations_DuplicateIds_RejectsAmbiguousResult()
    {
        var json = """
            {
                "translations": [
                    { "id": 1, "text": "first" },
                    { "id": 1, "text": "second" }
                ]
            }
            """;

        Assert.Throws<InvalidOperationException>(() => TranslationResultParser.ParseTranslations(json));
    }

    [Fact]
    public void ParseTranslations_NoJsonAtAll_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => TranslationResultParser.ParseTranslations("no json here"));
    }

    [Fact]
    public void ParseTranslations_WhitespaceAroundJson_Parses()
    {
        var json = """

            {"translations": [{"id": 100, "text": "테스트"}]}

            """;

        var result = TranslationResultParser.ParseTranslations(json);

        Assert.Single(result);
        Assert.Equal("테스트", result[100]);
    }
}
