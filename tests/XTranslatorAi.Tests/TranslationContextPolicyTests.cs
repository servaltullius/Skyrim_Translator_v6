using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public class TranslationContextPolicyTests
{
    [Fact]
    public void BookTitleMatch_IsExactAndAmbiguousTitlesAreOmitted()
    {
        var titles = TranslationBookContext.CollectTitles(new (string? Rec, string? Edid, string Source)[]
        {
            ("BOOK:FULL", "Book01", "Title One"), ("BOOK:FULL", "Book02", "Title Two"),
            ("BOOK:FULL", "Book02", "Conflicting Title"), ("MESG:FULL", "Book03", "Not a book"),
        });
        Assert.Equal("Title One", Assert.Single(titles).Value);
        Assert.False(titles.ContainsKey("Book"));
        Assert.False(titles.ContainsKey("Book02"));
    }

    [Fact]
    public void Reference_IsBoundedAndCannotInjectProtectedTokensOrMarkup()
    {
        var reference = TranslationBookContext.Build(new string('T', 300),
            "__XT_PH_0000__ <b>" + new string('P', 1000) + "</b>",
            "<mag>[pagebreak]__XT_TERM_0001__" + new string('N', 1000));
        Assert.NotNull(reference);
        Assert.True(reference.Length < 1300);
        Assert.DoesNotContain("__XT_", reference);
        Assert.DoesNotContain("<", reference);
        Assert.DoesNotContain("[pagebreak]", reference);
        Assert.Contains(new string('P', 400), reference);
    }

    [Fact]
    public void BookTitleAndBody_ReceiveDifferentStylesFromSharedPolicy()
    {
        Assert.Contains("book title", TranslationStyleHints.Get("A History", "BOOK:FULL"));
        Assert.Contains("문어체", TranslationStyleHints.Get("A History", "BOOK:DESC"));
        Assert.Contains("spoken Korean", TranslationStyleHints.Get("Hello", "INFO:NAM1"));
    }
}
