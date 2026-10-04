using System.Collections.Generic;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.Lqa.Internal;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Written narration in 합니다체 often closes with a rhetorical "-ㄹ까요?" ("누가 알았을까요?"). Only the last
/// sentence was classified, so these rows were reported as 해요체 among 합니다체 books.
/// </summary>
public class RecToneRhetoricalQuestionTests
{
    private static readonly Dictionary<string, ToneKind> BookMajority = new() { ["BOOK:DESC"] = ToneKind.Hamnida };

    private static List<LqaIssue> Check(string dest)
    {
        var issues = new List<LqaIssue>();
        var entry = new LqaScanEntry(1, 1, "Book01", "BOOK:DESC", StringEntryStatus.Done, "src", dest);
        RecToneRule.Apply(entry, "src", dest, BookMajority, issues);
        return issues;
    }

    [Theory]
    [InlineData("그는 그녀를 살려두었습니다. 어째서 그녀를 도왔던 걸까요?")]
    [InlineData("그 장면을 떠올리게 됩니다. 누가 알았을까요?")]
    public void RhetoricalQuestionAfterHamnida_IsNotReported(string dest)
        => Assert.Empty(Check(dest));

    [Theory]
    [InlineData("어떤 제임스인가요?")]                       // the whole row is 해요체
    [InlineData("그는 떠났어요. 다시 올까요?")]               // the sentence before is 해요체 too
    public void HaeyoRow_IsStillReported(string dest)
        => Assert.Single(Check(dest));
}
