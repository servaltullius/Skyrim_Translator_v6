using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.ProjectContext;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectContextScannerTests
{
    [Fact]
    public async Task ScanAsync_BuildsStableTopRec_Terms_AndSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);

            await db.UpsertGlossaryAsync(
                new GlossaryUpsertRequest(
                    Category: "Test",
                    SourceTerm: "Saarthal",
                    TargetTerm: "사아쌀",
                    Enabled: true,
                    Priority: 10,
                    MatchMode: GlossaryMatchMode.WordBoundary,
                    ForceMode: GlossaryForceMode.ForceToken,
                    Note: null
                ),
                CancellationToken.None
            );

            await db.BulkInsertStringsAsync(
                new[]
                {
                    CreateRow(1, "INFO:NAM1", "Saarthal Amulet"),
                    CreateRow(2, "INFO:NAM1", "Saarthal Amulet"),
                    CreateRow(3, "INFO:NAM1", "Saarthal Amulet"),
                    CreateRow(4, "MGEF", "Absorb <mag> points."),
                },
                CancellationToken.None
            );

            var scanner = new ProjectContextScanner();
            var report = await scanner.ScanAsync(
                db,
                globalDb: null,
                options: new ProjectContextScanOptions(
                    AddonName: "Dummy",
                    InputFile: "dummy.xml",
                    SourceLang: "english",
                    TargetLang: "korean",
                    NexusContext: null
                ),
                cancellationToken: CancellationToken.None
            );

            Assert.Equal(4, report.TotalStrings);

            Assert.True(report.TopRec.Count >= 2);
            Assert.Equal("INFO:NAM1", report.TopRec[0].Rec);
            Assert.Equal(3, report.TopRec[0].Count);

            Assert.Contains(report.TopTerms, t => t.Source == "Saarthal" && t.Count == 3 && t.Target == "사아쌀");

            Assert.Contains(report.Samples, s => s.Rec == "MGEF" && s.Text.Contains("<mag>", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDbFiles(path);
        }
    }

    private static (int OrderIndex, string? ListAttr, string? PartialAttr, string? AttributesJson, string? Edid, string? Rec, string SourceText, string DestText, StringEntryStatus Status, string RawStringXml) CreateRow(
        int orderIndex,
        string rec,
        string sourceText
    )
    {
        return (
            OrderIndex: orderIndex,
            ListAttr: null,
            PartialAttr: null,
            AttributesJson: null,
            Edid: null,
            Rec: rec,
            SourceText: sourceText,
            DestText: "",
            Status: StringEntryStatus.Pending,
            RawStringXml: "<r/>"
        );
    }

    private static async Task SeedProjectAsync(ProjectDb db)
    {
        var now = DateTimeOffset.UtcNow;
        await db.UpsertProjectAsync(
            new ProjectInfo(
                Id: 1,
                InputXmlPath: "C:\\dummy.xml",
                AddonName: "Dummy",
                Franchise: null,
                SourceLang: "english",
                DestLang: "korean",
                XmlVersion: "1",
                XmlHasBom: false,
                XmlPrologLine: "<?xml version=\"1.0\"?>",
                ModelName: "gemini-3-flash-preview",
                BasePromptText: "base",
                CustomPromptText: null,
                UseCustomPrompt: false,
                CreatedAt: now,
                UpdatedAt: now
            ),
            CancellationToken.None
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

    private static void TryDeleteDbFiles(string path)
    {
        TryDelete(path);
        TryDelete(path + "-wal");
        TryDelete(path + "-shm");
    }
}
