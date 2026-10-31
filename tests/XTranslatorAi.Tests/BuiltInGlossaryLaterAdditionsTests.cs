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

    // IDE Thalmor: the Divine Prosecution is an institution; 검찰관 (a prosecutor) made "신성 검찰관의 장교".
    [Theory]
    [InlineData("신성 검찰관", "Built-in default glossary", "신성 검찰")]
    [InlineData("신성 검찰관", null, "신성 검찰관")]
    [InlineData("신성 검찰청", "Built-in default glossary", "신성 검찰청")]
    public async Task DivineProsecution_IsCorrectedToTheInstitution_OnlyWhereTheUserKeptTheOldOne(string target, string? note, string expected)
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)"진영 및 단체 (Factions/Groups)", "Divine Prosecution", target, true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, note) },
            CancellationToken.None);

        await AddAllBatchesAsync();

        Assert.Equal(expected, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Divine Prosecution").TargetTerm);
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

    // Decided 2026-10-05 (official translation: Iron Boots 철 전투화, Fortify Smithing 제련 강화).
    [Theory]
    [InlineData("Boots", "부츠", "Built-in default glossary", "전투화")]
    [InlineData("Smithing", "대장", "Built-in default glossary", "제련")]
    [InlineData("Fortify Smithing", "대장 강화", "Built-in default glossary", "제련 강화")]
    [InlineData("Boots", "부츠", null, "부츠")]
    public async Task BootsAndSmithing_AreCorrected_OnlyWhereTheUserKeptTheOldOne(string source, string target, string? note, string expected)
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)null, source, target, true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, note) },
            CancellationToken.None);

        await AddAllBatchesAsync();

        Assert.Equal(expected, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == source).TargetTerm);
    }

    // Decided 2026-10-05 from the Feris review: the game (built-in TM) names the cave 부풀은 사내의 암굴; the patch has
    // since spelled it 부푼 사내의 암굴, and a glossary from before either batch goes straight to that.
    [Theory]
    [InlineData("Built-in default glossary", "부푼 사내의 암굴")]
    [InlineData(null, "부풀은 사내 암굴")]
    public async Task BloatedMansGrotto_IsCorrected_OnlyWhereTheUserKeptTheOldOne(string? note, string expected)
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)null, "Bloated Man's Grotto", "부풀은 사내 암굴", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, note) },
            CancellationToken.None);

        await AddAllBatchesAsync();

        Assert.Equal(expected, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Bloated Man's Grotto").TargetTerm);
    }

    [Fact]
    public async Task NewGlossary_GetsTheGameNameOfBloatedMansGrotto()
    {
        await new BuiltInGlossaryService().EnsureBuiltInGlossaryAsync(_db, CancellationToken.None);

        Assert.Equal("부푼 사내의 암굴", Assert.Single(await GlossaryAsync(), e => e.SourceTerm == "Bloated Man's Grotto").TargetTerm);
    }

    // The third review of the game translation (2026-10-31): the list's names that the game text spells otherwise.
    [Theory]
    [InlineData("Boethiah", "보에시아", "Built-in default glossary", "보에디아")]
    [InlineData("Chaurus", "차루스", "Built-in default glossary", "챠루스")]
    [InlineData("Block", "막기", "Built-in default glossary (prompt-only default)", "방어")]
    [InlineData("Akaviri", "아카비르", "Built-in default glossary", "아카비리")]
    [InlineData("Bloated Man's Grotto", "부풀은 사내의 암굴", "Built-in default glossary", "부푼 사내의 암굴")]
    [InlineData("Boethiah", "보에시아", null, "보에시아")]
    [InlineData("Boethiah", "보에씨아", "Built-in default glossary", "보에씨아")]
    public async Task GameTextNames_AreCorrected_OnlyWhereTheUserKeptTheOldOne(string source, string target, string? note, string expected)
        => await CorrectedAsync(source, target, note, expected);

    // The continent and the old tongue get their own entries beside the people's 아카비리 and 알드머.
    [Fact]
    public async Task AkavirAndAldmeris_AreAddedToExistingGlossaries()
    {
        await AddAllBatchesAsync();
        var existing = await GlossaryAsync();
        Assert.Equal("아카비르", Assert.Single(existing, e => e.SourceTerm == "Akavir").TargetTerm);
        Assert.Equal("알드메리스", Assert.Single(existing, e => e.SourceTerm == "Aldmeris").TargetTerm);
    }

    private async Task CorrectedAsync(string source, string target, string? note, string expected)
    {
        await _db.BulkInsertGlossaryAsync(
            new[] { ((string?)null, source, target, true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, note) },
            CancellationToken.None);

        await AddAllBatchesAsync();

        Assert.Equal(expected, Assert.Single(await GlossaryAsync(), e => e.SourceTerm == source).TargetTerm);
    }

    // Decided 2026-10-05 from the Elden Rim review: [Activate Button] 활성화 버튼 (8 rows), [Sprint Button] 질주 버튼.
    [Fact]
    public async Task ButtonNames_AreAddedToExistingAndNewGlossaries()
    {
        await AddAllBatchesAsync();
        var existing = await GlossaryAsync();
        Assert.Equal("활성화 버튼", Assert.Single(existing, e => e.SourceTerm == "Activate Button").TargetTerm);
        Assert.Equal("질주 버튼", Assert.Single(existing, e => e.SourceTerm == "Sprint Button").TargetTerm);
    }

    [Fact]
    public async Task NewGlossary_GetsBootsAndSmithingAsDecided()
    {
        await new BuiltInGlossaryService().EnsureBuiltInGlossaryAsync(_db, CancellationToken.None);

        var glossary = await GlossaryAsync();
        Assert.Equal("전투화", Assert.Single(glossary, e => e.SourceTerm == "Boots").TargetTerm);
        Assert.Equal("제련", Assert.Single(glossary, e => e.SourceTerm == "Smithing").TargetTerm);
        Assert.Equal("제련 강화", Assert.Single(glossary, e => e.SourceTerm == "Fortify Smithing").TargetTerm);
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

    /// <summary>
    /// The speech-check tags of dialogue choices reach existing glossaries as the game translation writes them, and
    /// match only with their parentheses: "brawling" and "persuade him" are plain words.
    /// </summary>
    [Fact]
    public async Task DialogueChoiceTags_AreAddedAndMatchOnlyInParentheses()
    {
        await new BuiltInGlossaryService().AddLaterEntriesOnceAsync(_db, StampPathFor("2026-10-18.2"), "2026-10-18.2",
            BethesdaFranchise.ElderScrolls, CancellationToken.None);

        var tags = (await GlossaryAsync()).Where(e => e.SourceTerm.StartsWith('(')).ToDictionary(e => e.SourceTerm, e => e.TargetTerm);
        Assert.Equal("(설득)", tags["(Persuade)"]);
        Assert.Equal("(위협)", tags["(Intimidate)"]);
        Assert.Equal("(뇌물)", tags["(Bribe)"]);
        Assert.Equal("(거짓)", tags["(Lie)"]);
        Assert.Equal("(싸움)", tags["(Brawl)"]);

        var applied = new GlossaryApplier(await GlossaryAsync()).Apply("Let's settle this. (Brawl) I came for the brawling tourney. (Persuade) Persuade him.");
        Assert.Equal(2, applied.TokenToReplacement.Count(t => t.Value is "(싸움)" or "(설득)"));
        Assert.Contains("brawling", applied.Text, StringComparison.Ordinal);
        Assert.Contains("Persuade him", applied.Text, StringComparison.Ordinal);
    }

    private Task AddLaterEntriesAsync()
        => new BuiltInGlossaryService().AddLaterEntriesOnceAsync(_db, StampPath, "2026-10-01", BethesdaFranchise.ElderScrolls, CancellationToken.None);

    private async Task<GlossaryEntry[]> GlossaryAsync()
        => (await _db.GetGlossaryAsync(CancellationToken.None)).ToArray();
}
