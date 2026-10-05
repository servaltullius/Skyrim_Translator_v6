using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;

namespace XTranslatorAi.Tests;

// How translation memory entries are written, replaced and reused: manual edits, retranslation,
// source-key normalization and conflicting entries for one source.
public sealed class TranslationMemoryUpdateTests
{
    private const string Lang = "english";

    [Fact]
    public async Task ManualEdit_ReplacesTmTarget_AndLaterRunUsesTheLatestEdit()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Iron Sword", "WEAP:FULL"), ("Iron Sword", "WEAP:FULL"));
        var (edited, pending) = (fixture.Ids[0], fixture.Ids[1]);

        await fixture.Db.UpdateStringTranslationAsync(edited, "First edit", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(edited, "Second edit", StringEntryStatus.Edited, null, CancellationToken.None);

        var entry = Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None));
        Assert.Equal("Iron Sword", entry.SourceText);
        Assert.Equal("Second edit", entry.DestText);

        // The project's own edit also beats a series (franchise) TM entry for the same source.
        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            Ids = new[] { pending },
            GlobalTranslationMemory = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TranslationMemoryKey.NormalizeSource("Iron Sword")] = "Series translation",
            },
        });

        var row = (await fixture.RowsAsync())[pending];
        Assert.Equal(StringEntryStatus.Done, row.Status);
        Assert.Equal("Second edit", row.DestText);
        Assert.Equal(0, fixture.Client.Calls);
        Assert.Contains(pending, (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.TmHitNoteKind, CancellationToken.None)).Keys);
    }

    [Fact]
    public async Task BatchedManualEdits_UpdateTm_ButFinishedTranslationsDoNot()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Iron Sword", "WEAP:FULL"), ("Steel Sword", "WEAP:FULL"));
        var (iron, steel) = (fixture.Ids[0], fixture.Ids[1]);

        await fixture.Db.UpdateStringTranslationsAsync(
            new (long, string, StringEntryStatus, string?)[]
            {
                (iron, "Edited iron", StringEntryStatus.Edited, null),
                (steel, "Machine steel", StringEntryStatus.Done, null),
            },
            CancellationToken.None
        );

        var tm = await fixture.Db.GetTranslationMemoryAsync(Lang, Lang, CancellationToken.None);
        Assert.Equal("Edited iron", Assert.Single(tm, kv => kv.Key == "iron sword").Value);
        Assert.False(tm.ContainsKey("steel sword"));
    }

    // "후처리 재적용" over every edited row of a big project: the source lookup put all ids in one IN list, and past
    // SQLite's 32,766 variables the call failed after the rows were already saved, so the TM got nothing.
    [Fact]
    public async Task ManyManualEdits_AreSavedWithTheirTmEntries()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Iron Sword", "WEAP:FULL"));
        var rows = Enumerable.Range(0, 33000).Select(i => (OrderIndex: i + 10, ListAttr: (string?)null, PartialAttr: (string?)null,
            AttributesJson: (string?)null, Edid: (string?)null, Rec: (string?)"MESG", SourceText: $"Message {i}", DestText: "",
            Status: StringEntryStatus.Pending, RawStringXml: "<r/>")).ToArray();
        await fixture.Db.BulkInsertStringsAsync(rows, CancellationToken.None);
        var ids = await fixture.Db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);

        await fixture.Db.UpdateStringTranslationsAsync(
            ids.Select(id => (id, "수정", StringEntryStatus.Edited, (string?)null)).ToArray(), CancellationToken.None);

        var tm = await fixture.Db.GetTranslationMemoryAsync(Lang, Lang, CancellationToken.None);
        Assert.True(tm.Count >= 33000, $"TM has {tm.Count} entries");
    }

    [Fact]
    public async Task RetranslatingOrRevertingAnEditedRow_KeepsTheEditAsTheTmEntry()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Iron Sword", "WEAP:FULL"), ("Iron Sword", "WEAP:FULL"));
        var (edited, other) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.UpdateStringTranslationAsync(edited, "User edit", StringEntryStatus.Edited, null, CancellationToken.None);

        // Later machine writes, an empty "edit" and a reset do not touch or wipe the entry.
        await fixture.Db.UpdateStringTranslationAsync(edited, "Machine text", StringEntryStatus.Done, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(edited, "  ", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringStatusAsync(edited, StringEntryStatus.Pending, errorMessage: null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(edited, "User edit", StringEntryStatus.Edited, null, CancellationToken.None);
        Assert.Equal(1, await fixture.Db.ResetForRetranslationAsync(new[] { edited }, includeEdited: true, CancellationToken.None));

        var entry = Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None));
        Assert.Equal("User edit", entry.DestText);

        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = new[] { edited, other } });

        var rows = await fixture.RowsAsync();
        Assert.All(new[] { rows[edited], rows[other] }, row =>
        {
            Assert.Equal(StringEntryStatus.Done, row.Status);
            Assert.Equal("User edit", row.DestText);
        });
        Assert.Equal(0, fixture.Client.Calls);
        // Applying a TM hit writes the row as Done, which must not rewrite the entry either.
        Assert.Equal("User edit", Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None)).DestText);
    }

    [Theory]
    [InlineData("Iron Sword", "iron sword")]
    [InlineData("  Iron Sword \t", "iron sword")]
    [InlineData("Iron Sword\r\n", "iron sword")]
    [InlineData("Line one\r\nLine two", "line one\nline two")]
    [InlineData("Line one\rLine two", "line one\nline two")]
    [InlineData("Line one\nLine two", "line one\nline two")]
    [InlineData("Iron  Sword", "iron  sword")]
    [InlineData(" \r\n ", "")]
    public void NormalizeSource_TrimsFoldsLineEndingsAndCase_ButKeepsInnerSpacing(string source, string expected)
    {
        Assert.Equal(expected, TranslationMemoryKey.NormalizeSource(source));
    }

    [Theory]
    [InlineData("Line one\nLine two", "Line one\r\nLine two", "First line\nSecond line", "First line\r\nSecond line")]
    [InlineData("Line one\r\nLine two", "Line one\nLine two", "First line\r\nSecond line", "First line\nSecond line")]
    [InlineData("Line one\r\nLine two", "Line one\rLine two", "First line\r\nSecond line", "First line\rSecond line")]
    public async Task TmEntry_HitsTheSameSourceWithOtherLineEndings(
        string editedSource, string pendingSource, string editedText, string expected)
    {
        await using var fixture = await TranslationRunFixture.CreateAsync((editedSource, "MESG:DESC"), (pendingSource, "MESG:DESC"));
        var (edited, pending) = (fixture.Ids[0], fixture.Ids[1]);
        await fixture.Db.UpdateStringTranslationAsync(edited, editedText, StringEntryStatus.Edited, null, CancellationToken.None);

        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = new[] { pending } });

        var row = (await fixture.RowsAsync())[pending];
        Assert.Equal(StringEntryStatus.Done, row.Status);
        Assert.Equal(expected, row.DestText);
        Assert.Equal(0, fixture.Client.Calls);
        Assert.DoesNotContain(pending, (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.TmFallbackNoteKind, CancellationToken.None)).Keys);
    }

    [Fact]
    public async Task TmEntry_HitsSourceThatDiffersOnlyInCaseOrSurroundingSpaces()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("Iron Sword", "WEAP:FULL"), ("IRON SWORD", "WEAP:FULL"), (" Iron Sword ", "WEAP:FULL"));
        await fixture.Db.UpdateStringTranslationAsync(fixture.Ids[0], "User edit", StringEntryStatus.Edited, null, CancellationToken.None);

        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = new[] { fixture.Ids[1], fixture.Ids[2] } });

        var rows = await fixture.RowsAsync();
        Assert.Equal("User edit", rows[fixture.Ids[1]].DestText);
        Assert.Equal("User edit", rows[fixture.Ids[2]].DestText);
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Fact]
    public async Task TmEntry_WithMismatchedLineBreakCount_StillFallsBackToModel()
    {
        // Only the line-ending style is adapted; a translation with a missing line break stays rejected.
        await using var fixture = await TranslationRunFixture.CreateAsync(("Line one\nLine two", "MESG:DESC"), ("Line one\r\nLine two", "MESG:DESC"));
        await fixture.Db.UpdateStringTranslationAsync(fixture.Ids[0], "One line only", StringEntryStatus.Edited, null, CancellationToken.None);

        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = new[] { fixture.Ids[1] } });

        var row = (await fixture.RowsAsync())[fixture.Ids[1]];
        Assert.Equal("Line one\r\nLine two", row.DestText);
        Assert.True(fixture.Client.Calls > 0);
        Assert.Contains(fixture.Ids[1], (await fixture.Db.GetStringNotesByKindAsync(TranslationConstants.TmFallbackNoteKind, CancellationToken.None)).Keys);
    }

    [Fact]
    public async Task ConflictingEditsForOneSource_LastEditWins_WithOneEntry()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("Iron Sword", "WEAP:FULL"), ("iron sword ", "WEAP:FULL"), ("Iron Sword", "WEAP:FULL"));
        var (first, second, pending) = (fixture.Ids[0], fixture.Ids[1], fixture.Ids[2]);

        await fixture.Db.UpdateStringTranslationAsync(first, "Edit from first row", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Db.UpdateStringTranslationAsync(second, "Edit from second row", StringEntryStatus.Edited, null, CancellationToken.None);

        var entry = Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None));
        Assert.Equal("iron sword ", entry.SourceText);
        Assert.Equal("Edit from second row", entry.DestText);

        // Editing the first row again makes it the latest and therefore the winner.
        await fixture.Db.UpdateStringTranslationAsync(first, "Edit from first row", StringEntryStatus.Edited, null, CancellationToken.None);
        await fixture.Service.TranslateIdsAsync(fixture.Request with { Ids = new[] { pending } });

        Assert.Equal("Edit from first row", (await fixture.RowsAsync())[pending].DestText);
        Assert.Equal(0, fixture.Client.Calls);
    }

    [Fact]
    public async Task BulkUpsert_SameSourceTwiceInOneImport_KeepsTheLaterPairAndOverwritesOlderEntries()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(("Unused", "WEAP:FULL"));
        await fixture.Db.BulkUpsertTranslationMemoryAsync(Lang, Lang, new[] { ("Iron Sword", "Old import") }, CancellationToken.None);

        var applied = await fixture.Db.BulkUpsertTranslationMemoryAsync(
            "English",
            " ENGLISH ",
            new[] { ("Iron Sword", "Earlier pair"), ("IRON SWORD\r\n", "Later pair"), ("Steel Sword", " "), ("  ", "Ignored") },
            CancellationToken.None
        );

        Assert.Equal(2, applied);
        var entry = Assert.Single(await fixture.Db.GetTranslationMemoryEntriesAsync(Lang, Lang, CancellationToken.None));
        Assert.Equal("IRON SWORD\r\n", entry.SourceText);
        Assert.Equal("Later pair", entry.DestText);
        Assert.Equal("Later pair", (await fixture.Db.GetTranslationMemoryAsync(Lang, Lang, CancellationToken.None))["iron sword"]);
    }
}
