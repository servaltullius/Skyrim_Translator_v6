using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using Xunit;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

public class ProjectDbCultureTests
{
    [Theory]
    [InlineData("th-TH")] // Buddhist calendar
    [InlineData("ar-SA")] // Hijri calendar
    [InlineData("de-DE")]
    public async Task StoredTimestamps_ReadBackUnderAnyUserCulture(string culture)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-culture-{Guid.NewGuid():N}.sqlite");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            var now = DateTimeOffset.UtcNow;
            await db.UpsertProjectAsync(new ProjectInfo(1, "dummy.xml", "Dummy", null, "english", "korean",
                "1", false, "<?xml version=\"1.0\"?>", "gemini-3.8-flash", "base", null, false, now, now), CancellationToken.None);
            await db.BulkInsertStringsAsync(new[] { (
                OrderIndex: 0, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null,
                Edid: (string?)null, Rec: (string?)"BOOK:FULL", SourceText: "Hello", DestText: "",
                Status: StringEntryStatus.Pending, RawStringXml: "<r/>") }, CancellationToken.None);

            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var entry = Assert.Single(await db.GetStringsAsync(10, 0, CancellationToken.None));

            Assert.InRange(entry.UpdatedAt.UtcDateTime.Year, now.Year - 1, now.Year + 1);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }
}
