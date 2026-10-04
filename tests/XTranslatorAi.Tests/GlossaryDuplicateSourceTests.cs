using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

/// <summary>
/// Adding or importing a term whose source the glossary already holds used to insert a second row that never
/// applied (rows apply by priority, then length, then age, so the older row replaced the text first) while the
/// screen reported success.
/// </summary>
public sealed class GlossaryDuplicateSourceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-glossary-dup-" + Guid.NewGuid().ToString("N"));
    private ProjectDb _db = null!;
    private string DbPath => Path.Combine(_root, "project.sqlite");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _db = await ProjectDb.OpenOrCreateAsync(DbPath, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(DbPath);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Add_WithAnExistingSource_UpdatesThatEntryAndTheNewTargetApplies()
    {
        await _db.UpsertGlossaryAsync(Forced("Whiterun", "화이트 런"), CancellationToken.None);

        var outcome = await _db.UpsertGlossaryAsync(Forced("whiterun", "화이트런", category: null), CancellationToken.None);

        Assert.Equal(new GlossaryUpsertOutcome(Updated: true, PreviousTarget: "화이트 런"), outcome);
        var entry = Assert.Single(await _db.GetGlossaryAsync(CancellationToken.None));
        Assert.Equal(("whiterun", "화이트런", "Names"), (entry.SourceTerm, entry.TargetTerm, entry.Category));
        Assert.Equal("화이트런", Applied("Go to Whiterun."));
    }

    [Fact]
    public async Task Add_OverEarlierDuplicates_ChangesTheRowThatApplies()
    {
        // Duplicates the plain INSERT already left in existing glossaries.
        await _db.BulkInsertGlossaryAsync(new[]
        {
            ((string?)null, "Riften", "리프텐", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)null),
            ((string?)null, "Riften", "리프튼", true, 10, (int)GlossaryMatchMode.WordBoundary, (int)GlossaryForceMode.ForceToken, (string?)null),
        }, CancellationToken.None);

        var outcome = await _db.UpsertGlossaryAsync(Forced("Riften", "리프트"), CancellationToken.None);

        Assert.Equal("리프텐", outcome.PreviousTarget);
        Assert.Equal("리프트", Applied("Riften guard"));
    }

    [Fact]
    public async Task Add_AnotherPromptOnlyTarget_IsKeptBesideTheOthers()
    {
        await _db.UpsertGlossaryAsync(PromptOnly("Hearthfire", "9월"), CancellationToken.None);

        var added = await _db.UpsertGlossaryAsync(PromptOnly("Hearthfire", "허스파이어"), CancellationToken.None);
        var again = await _db.UpsertGlossaryAsync(PromptOnly("Hearthfire", "허스파이어", priority: 30), CancellationToken.None);

        Assert.False(added.Updated);
        Assert.True(again.Updated);
        var entries = await _db.GetGlossaryAsync(CancellationToken.None);
        Assert.Equal(new[] { ("9월", 10), ("허스파이어", 30) },
            entries.Select(e => (e.TargetTerm, e.Priority)).OrderBy(e => e.TargetTerm, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Import_ReportsSourcesAlreadyHeldWithAnotherTarget_InsteadOfInsertingThem()
    {
        await _db.UpsertGlossaryAsync(Forced("Whiterun", "화이트 런"), CancellationToken.None);
        await _db.UpsertGlossaryAsync(PromptOnly("Hearthfire", "9월"), CancellationToken.None);
        var tsv = Path.Combine(_root, "terms.tsv");
        await File.WriteAllTextAsync(tsv, "Source\tTarget\nWhiterun\t화이트런\nWhiterun Hold\t화이트런 지방\nwhiterun\t화이트 런\n");

        var result = await new GlossaryImportService(new GlossaryFileService()).ImportFromFileAsync(_db, tsv,
            new GlossaryImportService.GlossaryImportOptions(10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null),
            CancellationToken.None);

        // "Whiterun" has two targets in the file itself, so it is a file conflict; "Whiterun Hold" is new.
        Assert.Equal((1, 0, 1), (result!.Value.InsertedCount, result.Value.SkippedExisting, result.Value.ConflictCount));
        await File.WriteAllTextAsync(tsv, "Source\tTarget\nWhiterun\t화이트런\nWhiterun Hold\t화이트런 지방\n");
        var second = await new GlossaryImportService(new GlossaryFileService()).ImportFromFileAsync(_db, tsv,
            new GlossaryImportService.GlossaryImportOptions(10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null),
            CancellationToken.None);

        Assert.Equal(0, second!.Value.InsertedCount);
        Assert.Equal(1, second.Value.SkippedExisting);
        Assert.Equal(new[] { "Whiterun" }, second.Value.ExistingConflicts);
        Assert.Equal("화이트 런", Applied("Go to Whiterun."));
        Assert.Equal(3, (await _db.GetGlossaryAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task GlobalGlossaryAdd_UpdatesTheExistingEntryToo()
    {
        var builtIn = new BuiltInGlossaryService();
        var globalRoot = Path.Combine(_root, "global");
        await using var global = new GlobalProjectDbService(builtIn, globalRoot);
        var service = new GlobalGlossaryService(global, new ProjectGlossaryService(new GlossaryImportService(new GlossaryFileService())));
        try
        {
            await service.UpsertAsync(Forced("Zz Test Source", "첫 번역"), CancellationToken.None);
            var outcome = await service.UpsertAsync(Forced("Zz Test Source", "둘째 번역"), CancellationToken.None);

            Assert.True(outcome.Updated);
            var matching = (await service.GetAsync(CancellationToken.None)).Where(e => e.SourceTerm == "Zz Test Source").ToList();
            Assert.Equal("둘째 번역", Assert.Single(matching).TargetTerm);
        }
        finally
        {
            await global.DisposeAsync();
            foreach (var db in Directory.GetFiles(globalRoot, "*.sqlite", SearchOption.AllDirectories))
            {
                TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
            }
        }
    }

    private string Applied(string text)
    {
        var glossary = _db.GetGlossaryAsync(CancellationToken.None).GetAwaiter().GetResult();
        return Assert.Single(new GlossaryApplier(glossary).Apply(text).TokenToReplacement).Value;
    }

    private static GlossaryUpsertRequest Forced(string source, string target, string? category = "Names")
        => new(category, source, target, true, 10, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, null);

    private static GlossaryUpsertRequest PromptOnly(string source, string target, int priority = 10)
        => new(null, source, target, true, priority, GlossaryMatchMode.WordBoundary, GlossaryForceMode.PromptOnly, null);
}
