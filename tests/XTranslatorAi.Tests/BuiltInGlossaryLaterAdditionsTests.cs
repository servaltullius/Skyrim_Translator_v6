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

    // The update pass for old built-in entries ran on every open, so a user's choices came back on each launch.
    [Fact]
    public async Task UpdatePass_SwitchesAnUntouchedEntryOnce_AndKeepsTheUsersLaterChoice()
    {
        await _db.BulkInsertGlossaryAsync(new[]
        {
            ((string?)null, "Dragon", "드래곤", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)"Built-in default glossary"),
            ((string?)null, "Block", "막기", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)"Built-in default glossary (prompt-only default)"),
        }, CancellationToken.None);

        await new BuiltInGlossaryService().EnsureBuiltInGlossaryAsync(_db, CancellationToken.None, insertMissingEntries: false);

        // Dragon was never switched; Block was switched earlier and the user set it back to ForceToken.
        Assert.Equal(GlossaryForceMode.PromptOnly, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Dragon").ForceMode);
        Assert.Equal(GlossaryForceMode.ForceToken, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Block").ForceMode);
    }

    [Fact]
    public async Task UpdatePass_IsSkippedOnceStamped()
    {
        await _db.BulkInsertGlossaryAsync(new[]
        {
            ((string?)null, "Smithing", "제련", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)"Built-in default glossary"),
        }, CancellationToken.None);

        await new BuiltInGlossaryService().EnsureBuiltInGlossaryAsync(_db, CancellationToken.None, insertMissingEntries: false, applyMigrations: false);

        Assert.Equal("제련", Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Smithing").TargetTerm);
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
        Assert.Equal("씨직 오더", Assert.Single(glossary, e => e.SourceTerm == "Psijic Order").TargetTerm);
        Assert.Equal("블랙-브라이어", Assert.Single(glossary, e => e.SourceTerm == "Black-Briar").TargetTerm);
        Assert.Equal("허닝브루", Assert.Single(glossary, e => e.SourceTerm == "Honningbrew").TargetTerm);
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

    // The official translation (built-in TM) has Fine 초급, matching Superior 중급 and Exquisite 상급.
    [Theory]
    [InlineData("하급", "Built-in default glossary", "초급")]
    [InlineData("하급", null, "하급")]
    [InlineData("하등급", "Built-in default glossary", "하등급")]
    public async Task FineTier_IsCorrectedToTheOfficialName_OnlyWhereTheUserKeptTheOldOne(string target, string? note, string expected)
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)null, "Fine", target, true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, note) },
            CancellationToken.None);

        await AddAllBatchesAsync();

        Assert.Equal(expected, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Fine").TargetTerm);
    }

    [Fact]
    public async Task NewGlossary_GetsTheOfficialFineTier()
    {
        await new BuiltInGlossaryService().EnsureBuiltInGlossaryAsync(_db, CancellationToken.None);

        Assert.Equal("초급", Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Fine").TargetTerm);
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
