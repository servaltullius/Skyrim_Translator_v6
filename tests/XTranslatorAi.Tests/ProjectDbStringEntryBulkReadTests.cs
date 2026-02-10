using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectDbStringEntryBulkReadTests
{
    [Fact]
    public async Task GetStringTranslationContextsByIdsAsync_ReturnsRowsForRequestedIds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await db.BulkInsertStringsAsync(
                new[]
                {
                    MakeRow(1, "REC_A", "EDID_A", "Source A", StringEntryStatus.Pending),
                    MakeRow(2, "REC_B", "EDID_B", "Source B", StringEntryStatus.Error),
                    MakeRow(3, "REC_C", "EDID_C", "Source C", StringEntryStatus.Done),
                },
                CancellationToken.None
            );

            var rows = await db.GetStringsAsync(limit: 100, offset: 0, CancellationToken.None);
            var ids = rows.Select(r => r.Id).ToArray();

            var contexts = await db.GetStringTranslationContextsByIdsAsync(ids, CancellationToken.None);

            Assert.Equal(ids.Length, contexts.Count);
            foreach (var row in rows)
            {
                Assert.True(contexts.TryGetValue(row.Id, out var ctx));
                Assert.Equal(row.Id, ctx.Id);
                Assert.Equal(row.SourceText, ctx.SourceText);
                Assert.Equal(row.Rec, ctx.Rec);
                Assert.Equal(row.Edid, ctx.Edid);
                Assert.Equal(row.Status, ctx.Status);
            }
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    [Fact]
    public async Task GetStringStatusesByIdsAsync_HandlesChunkedIdQueries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);

            const int rowCount = 1205; // larger than chunk size to exercise chunked IN queries.
            var rowsToInsert = new List<(
                int OrderIndex,
                string? ListAttr,
                string? PartialAttr,
                string? AttributesJson,
                string? Edid,
                string? Rec,
                string SourceText,
                string DestText,
                StringEntryStatus Status,
                string RawStringXml
            )>(capacity: rowCount);

            for (var i = 0; i < rowCount; i++)
            {
                var status = i % 2 == 0 ? StringEntryStatus.Pending : StringEntryStatus.Done;
                rowsToInsert.Add(MakeRow(i + 1, "REC", $"EDID_{i}", $"Source {i}", status));
            }

            await db.BulkInsertStringsAsync(rowsToInsert, CancellationToken.None);

            var inserted = await db.GetStringsAsync(limit: rowCount + 10, offset: 0, CancellationToken.None);
            var ids = inserted.Select(r => r.Id).ToArray();
            var expectedById = inserted.ToDictionary(r => r.Id, r => r.Status);

            var statuses = await db.GetStringStatusesByIdsAsync(ids, CancellationToken.None);

            Assert.Equal(ids.Length, statuses.Count);
            foreach (var id in ids)
            {
                Assert.True(statuses.TryGetValue(id, out var status));
                Assert.Equal(expectedById[id], status);
            }
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static (
        int OrderIndex,
        string? ListAttr,
        string? PartialAttr,
        string? AttributesJson,
        string? Edid,
        string? Rec,
        string SourceText,
        string DestText,
        StringEntryStatus Status,
        string RawStringXml
    ) MakeRow(
        int orderIndex,
        string rec,
        string edid,
        string sourceText,
        StringEntryStatus status
    )
    {
        return (
            OrderIndex: orderIndex,
            ListAttr: null,
            PartialAttr: null,
            AttributesJson: null,
            Edid: edid,
            Rec: rec,
            SourceText: sourceText,
            DestText: "",
            Status: status,
            RawStringXml: $"<String>{sourceText}</String>"
        );
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
