using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;

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

    /// <summary>
    /// Titles were collected only from the rows being translated, so a book whose title was translated in an
    /// earlier run had its body translated without the title.
    /// </summary>
    [Fact]
    public async Task BookTitles_ComeFromEveryTitleRowOfTheProject()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-book-titles-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await db.BulkInsertStringsAsync(new[]
            {
                (0, (string?)null, (string?)null, (string?)null, (string?)"BookLustyMaid", (string?)"BOOK:FULL",
                    "The Lusty Argonian Maid", "음탕한 아르고니안 하녀", StringEntryStatus.Done, "<String />"),
                (1, (string?)null, (string?)null, (string?)null, (string?)"BookLustyMaid", (string?)"BOOK:DESC",
                    "Lifts-Her-Tail: Certainly not, sir!", "", StringEntryStatus.Pending, "<String />"),
            }, CancellationToken.None);

            var titles = await TranslationRunnerService.CollectBookTitlesAsync(db, CancellationToken.None);

            Assert.Equal("The Lusty Argonian Maid", titles["BookLustyMaid"]);
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
        }
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
