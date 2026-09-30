using System.Reflection;
using XTranslatorAi.App.ViewModels;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Tests;

public sealed class LqaProtectedTextTests
{
    public static IEnumerable<object[]> InvalidTextPairs()
    {
        yield return new object[] { "You have %s gold.", "금화가 있습니다." };
        yield return new object[] { "Meet {name}.", "만나세요." };
        yield return new object[] { "First\r\nSecond", "첫째\n둘째" };
        yield return new object[] { "First\nSecond", "첫째 둘째" };
        yield return new object[] { "<b>First</b><i>Second</i>", "<i>첫째</i><b>둘째</b>" };
        yield return new object[] { "<b>First</b>\r\nSecond", "<b>첫째\r\n</b>둘째" };
        yield return new object[] { "Damage <mag>.", "피해 <MAG>." };
        yield return new object[] { "<b>Read this</b>", "Read this" };
        yield return new object[] { "Read this", "<b>Read this</b>" };
    }

    [Theory]
    [MemberData(nameof(InvalidTextPairs))]
    public async Task ScanAndMismatchFilter_ReportExportBlockingProtectionChanges(string source, string dest)
    {
        // Both direct-plugin REC names and existing XML REC values use the same contract.
        foreach (var rec in new[] { "INFO:NAM1", "INFO:00000800" })
        {
            var entry = new LqaScanEntry(42, 17, null, rec, StringEntryStatus.Edited, source, dest);
            var issues = await LqaScanner.ScanAsync(new[] { entry }, "korean", Array.Empty<GlossaryEntry>());
            var issue = Assert.Single(issues.Where(item => item.Code == "token_mismatch"));
            Assert.Equal("Error", issue.Severity);
            Assert.Equal(entry.Id, issue.Id); // LQA selection must lead back to this exact row.
            Assert.Equal(entry.OrderIndex, issue.OrderIndex);
            Assert.Equal(entry.Rec, issue.Rec);
            Assert.Equal(source, issue.SourceText);
            Assert.Equal(dest, issue.DestText);
        }

        Assert.True(InvokeUiFilter("HasTokenMismatch", source, dest));
    }

    [Theory]
    [InlineData("You have %s gold.", "금화 %s개가 있습니다.")]
    [InlineData("Meet {name}.", "{name} 만나세요.")]
    [InlineData("First\r\nSecond", "첫째\r\n둘째")]
    [InlineData("<b>First</b>\r\n<i>Second</i>", "<b>첫째</b>\r\n<i>둘째</i>")]
    [InlineData("Damage <mag> for <dur> seconds.", "<dur>초 동안 <mag> 피해.")]
    [InlineData("Read this", "읽으세요")]
    [InlineData("<b>Read this</b>", "<b>Read this</b>")]
    public async Task ScanAndMismatchFilter_AcceptPreservedProtection(string source, string dest)
    {
        var entry = new LqaScanEntry(42, 17, null, "INFO:NAM1", StringEntryStatus.Done, source, dest);
        var issues = await LqaScanner.ScanAsync(new[] { entry }, "korean", Array.Empty<GlossaryEntry>());
        Assert.DoesNotContain(issues, issue => issue.Code == "token_mismatch");
        Assert.False(InvokeUiFilter("HasTokenMismatch", source, dest));
    }

    [Theory]
    [InlineData("You have %s gold.", true)]
    [InlineData("Meet {name}.", true)]
    [InlineData("First\r\nSecond", true)]
    [InlineData("\n", true)]
    [InlineData("<b>Read this</b>", true)]
    [InlineData("[pagebreak]", true)]
    [InlineData("__XT_PH_0000__", true)]
    [InlineData("25% resistance", true)]
    [InlineData("Read this", false)]
    [InlineData("", false)]
    public void SourceProtectionFilter_IncludesVariablesAndLineBreaks(string source, bool expected)
    {
        Assert.Equal(expected, LqaScanner.HasProtectedText(source));
        Assert.Equal(expected, InvokeUiFilter("HasAnyUiTags", source));
    }

    private static bool InvokeUiFilter(string name, params object[] args)
        => (bool)typeof(MainViewModel).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, args)!;
}
