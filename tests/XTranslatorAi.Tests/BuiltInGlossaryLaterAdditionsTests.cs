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

    private string StampPath => ProjectPaths.GetBuiltInGlossaryAdditionsStampPath(
        Path.Combine(_root, "global-glossary.sqlite"), BuiltInGlossaryService.LaterAdditionsVersion);

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

    private Task AddLaterEntriesAsync()
        => new BuiltInGlossaryService().AddLaterEntriesOnceAsync(_db, StampPath, BethesdaFranchise.ElderScrolls, CancellationToken.None);

    private async Task<GlossaryEntry[]> GlossaryAsync()
        => (await _db.GetGlossaryAsync(CancellationToken.None)).ToArray();
}
