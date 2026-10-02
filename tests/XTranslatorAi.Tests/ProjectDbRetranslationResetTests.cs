using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectDbRetranslationResetTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"xt-retranslate-{Guid.NewGuid():N}.sqlite");
    private ProjectDb _db = null!;

    public async Task InitializeAsync()
    {
        _db = await ProjectDb.OpenOrCreateAsync(_path, CancellationToken.None);
        var rows = new[]
        {
            ("done", "번역됨", StringEntryStatus.Done),
            ("error", "", StringEntryStatus.Error),
            ("skipped", "기존 번역", StringEntryStatus.Skipped),
            ("edited", "직접 고침", StringEntryStatus.Edited),
            ("pending", "", StringEntryStatus.Pending),
        };
        await _db.BulkInsertStringsAsync(rows.Select((row, index) => (
            OrderIndex: index, ListAttr: (string?)null, PartialAttr: (string?)null, AttributesJson: (string?)null,
            Edid: (string?)row.Item1, Rec: (string?)"MGEF:FULL", SourceText: row.Item1, DestText: row.Item2,
            Status: row.Item3, RawStringXml: "<r/>"
        )).ToArray(), CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        TestDbHelper.TryDeleteDbFiles(_path);
    }

    [Fact]
    public async Task FinishedFailedAndSkippedRows_BecomePendingWithoutTheirTranslation()
    {
        var byEdid = await RowsAsync();
        await _db.UpsertStringNoteAsync(byEdid["done"].Id, TranslationConstants.TmHitNoteKind, "TM 적용", CancellationToken.None);

        var reset = await _db.ResetForRetranslationAsync(byEdid.Values.Select(row => row.Id).ToList(), includeEdited: false, CancellationToken.None);

        Assert.Equal(3, reset);
        var after = await RowsAsync();
        foreach (var edid in new[] { "done", "error", "skipped" })
        {
            Assert.Equal(StringEntryStatus.Pending, after[edid].Status);
            Assert.Equal("", after[edid].DestText);
        }

        Assert.Equal((StringEntryStatus.Edited, "직접 고침"), (after["edited"].Status, after["edited"].DestText));
        Assert.Empty(await _db.GetStringNotesByKindAsync(TranslationConstants.TmHitNoteKind, CancellationToken.None));
        var pendingForNextRun = await _db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, CancellationToken.None);
        Assert.Equal(4, pendingForNextRun.Count);
    }

    [Fact]
    public async Task ManualEdits_AreResetOnlyWhenIncluded()
    {
        var edited = (await RowsAsync())["edited"];

        Assert.Equal(1, await _db.ResetForRetranslationAsync(new[] { edited.Id }, includeEdited: true, CancellationToken.None));

        Assert.Equal(StringEntryStatus.Pending, (await RowsAsync())["edited"].Status);
    }

    private async Task<System.Collections.Generic.Dictionary<string, StringEntry>> RowsAsync()
        => (await _db.GetStringsAsync(100, 0, CancellationToken.None)).ToDictionary(row => row.Edid!);
}
