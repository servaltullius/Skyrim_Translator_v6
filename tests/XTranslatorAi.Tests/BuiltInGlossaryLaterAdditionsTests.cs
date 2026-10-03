using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// A global glossary is filled from the built-in list only when it is created, so entries added to
/// the list later (Elder Scroll) never reached existing installs and "Scroll" (주문서) was forced
/// inside the name.
/// </summary>
public class BuiltInGlossaryLaterAdditionsTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xtai-tests", Guid.NewGuid().ToString("N"));
    private ProjectDb _db = null!;

    private string StampPath => StampPathFor("2026-10-01");

    private string StampPathFor(string version) => ProjectPaths.GetBuiltInGlossaryAdditionsStampPath(
        Path.Combine(_root, "global-glossary.sqlite"), version);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _db = await ProjectDb.OpenOrCreateAsync(Path.Combine(_root, "global-glossary.sqlite"), CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ExistingGlossary_GetsTheLaterEntriesOnce()
    {
        await AddLaterEntriesAsync();

        var elderScroll = Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Elder Scroll");
        Assert.Equal("엘더스크롤", elderScroll.TargetTerm);
        Assert.Equal(GlossaryForceMode.ForceToken, elderScroll.ForceMode);
        Assert.Contains(await GlossaryAsync(), e => e.SourceTerm == "Elder Scrolls");
        Assert.True(File.Exists(StampPath));
    }

    [Fact]
    public async Task EntryDeletedByTheUser_IsNotAddedAgain()
    {
        await AddLaterEntriesAsync();
        var added = Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Elder Scroll");
        await _db.DeleteGlossaryEntryAsync(added.Id, CancellationToken.None);

        await AddLaterEntriesAsync();

        Assert.DoesNotContain(await GlossaryAsync(), e => e.SourceTerm == "Elder Scroll");
    }

    [Fact]
    public async Task UsersOwnTranslation_IsKept()
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)null, "Elder Scroll", "엘더 스크롤", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)null) },
            CancellationToken.None);

        await AddLaterEntriesAsync();

        Assert.Equal("엘더 스크롤", Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Elder Scroll").TargetTerm);
    }

    [Fact]
    public async Task LaterBatch_IsAddedEvenAfterAnEarlierOne_AndKeepsEarlierDeletions()
    {
        await AddLaterEntriesAsync();
        var added = Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Elder Scroll");
        await _db.DeleteGlossaryEntryAsync(added.Id, CancellationToken.None);

        await AddAllBatchesAsync();

        var glossary = await GlossaryAsync();
        Assert.DoesNotContain(glossary, e => e.SourceTerm == "Elder Scroll");
        Assert.Equal("길드 마스터", Assert.Single(glossary, e => e.SourceTerm == "Guild Master").TargetTerm);
        Assert.Equal("환영마법", Assert.Single(glossary, e => e.SourceTerm == "Illusion magic").TargetTerm);
        Assert.True(File.Exists(StampPathFor("2026-10-03")));
    }

    [Theory]
    [InlineData("몰라그 발", "Built-in default glossary", "몰락 발")]
    [InlineData("몰라그발", "Built-in default glossary", "몰라그발")]
    [InlineData("몰라그 발", null, "몰라그 발")]
    public async Task ChangedBuiltInTarget_IsCorrectedOnlyWhereTheUserKeptTheOldOne(string target, string? note, string expected)
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)"신화 및 주요 존재 (Mythology & Key Beings)", "Molag Bal", target, true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, note) },
            CancellationToken.None);

        await AddAllBatchesAsync();

        Assert.Equal(expected, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Molag Bal").TargetTerm);
    }

    private async Task AddAllBatchesAsync()
    {
        foreach (var batch in BuiltInGlossaryService.LaterAdditions)
        {
            await new BuiltInGlossaryService().AddLaterEntriesOnceAsync(_db, StampPathFor(batch.Version), batch.Version, BethesdaFranchise.ElderScrolls, CancellationToken.None);
        }
    }

    private Task AddLaterEntriesAsync()
        => new BuiltInGlossaryService().AddLaterEntriesOnceAsync(_db, StampPath, "2026-10-01", BethesdaFranchise.ElderScrolls, CancellationToken.None);

    private async Task<GlossaryEntry[]> GlossaryAsync()
        => (await _db.GetGlossaryAsync(CancellationToken.None)).ToArray();
}
