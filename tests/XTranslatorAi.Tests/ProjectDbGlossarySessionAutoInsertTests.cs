using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectDbGlossarySessionAutoInsertTests
{
    [Fact]
    public async Task TryInsertGlossaryIfMissingAsync_InsertsOnce_CaseInsensitive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);

            var inserted1 = await db.TryInsertGlossaryIfMissingAsync(
                request: CreateAutoGlossaryRequest("Ancient Dragons' Lightning Spear", "고룡의 뇌창"),
                cancellationToken: CancellationToken.None
            );
            Assert.True(inserted1);

            var inserted2 = await db.TryInsertGlossaryIfMissingAsync(
                request: CreateAutoGlossaryRequest("Ancient Dragons' Lightning Spear", "SHOULD_NOT_INSERT"),
                cancellationToken: CancellationToken.None
            );
            Assert.False(inserted2);

            var inserted3 = await db.TryInsertGlossaryIfMissingAsync(
                request: CreateAutoGlossaryRequest("ANCIENT DRAGONS' LIGHTNING SPEAR", "SHOULD_NOT_INSERT"),
                cancellationToken: CancellationToken.None
            );
            Assert.False(inserted3);

            var rows = await db.GetGlossaryAsync(CancellationToken.None);
            Assert.Single(rows);
            Assert.Equal("Ancient Dragons' Lightning Spear", rows[0].SourceTerm);
            Assert.Equal("고룡의 뇌창", rows[0].TargetTerm);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task TryInsertGlossaryIfMissingAsync_TreatsAccentedCaseAndSpacesAsTheSameSource()
    {
        // SQLite's LOWER folds only ASCII and keeps spaces, so these learned terms used to become second rows
        // beside the entries that already hold them.
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            Assert.True(await db.TryInsertGlossaryIfMissingAsync(CreateAutoGlossaryRequest("Ébène Guard", "흑단 경비대"), CancellationToken.None));
            Assert.True(await db.TryInsertGlossaryIfMissingAsync(CreateAutoGlossaryRequest("Whiterun ", "화이트런"), CancellationToken.None));

            Assert.False(await db.TryInsertGlossaryIfMissingAsync(CreateAutoGlossaryRequest("ÉBÈNE GUARD", "X"), CancellationToken.None));
            Assert.False(await db.TryInsertGlossaryIfMissingAsync(CreateAutoGlossaryRequest("Whiterun", "X"), CancellationToken.None));

            Assert.Equal(2, (await db.GetGlossaryAsync(CancellationToken.None)).Count);
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    private static GlossaryUpsertRequest CreateAutoGlossaryRequest(string source, string target)
        => new(
            Category: "Auto(Session)",
            SourceTerm: source,
            TargetTerm: target,
            Enabled: true,
            Priority: 20,
            MatchMode: GlossaryMatchMode.Substring,
            ForceMode: GlossaryForceMode.ForceToken,
            Note: "test"
        );
}
