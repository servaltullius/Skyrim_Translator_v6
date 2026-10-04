using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.ProjectContext;
using XTranslatorAi.Tests.TestSupport;
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
                    TargetLang: "korean"
                ),
                cancellationToken: CancellationToken.None
            );

            Assert.Equal(4, report.TotalStrings);

            Assert.True(report.TopRec.Count >= 2);
            Assert.Equal("INFO:NAM1", report.TopRec[0].Rec);
            Assert.Equal(3, report.TopRec[0].Count);

            Assert.Contains(report.TopTerms, t => t.Source == "Saarthal" && t.Count == 3 && t.Target == "사아쌀");

            Assert.Contains(report.Samples, s => s.Rec == "MGEF" && s.Text.Contains("<mag>", StringComparison.Ordinal));
            Assert.Single(report.Samples); // Existing special-sample reports keep their payload unchanged.
        }
        finally
        {
            TestDbHelper.TryDeleteDbFiles(path);
        }
    }

    [Fact]
    public async Task ScanAsync_WithoutSpecialSamples_ProvidesBoundedDistinctOriginalText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-context-plain-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);
            await SeedProjectAsync(db);
            var rows = new[]
            {
                CreateRow(0, "WEAP:FULL", "Iron Sword"),
                CreateRow(1, "WEAP:FULL", "Iron Sword"),
                CreateRow(2, "WEAP:FULL", "Iron Mace"),
                CreateRow(3, "WEAP:FULL", "Iron Dagger"),
                CreateRow(4, "BOOK:DESC", new string('x', 500)),
            }.Concat(Enumerable.Range(0, 12).Select(i => CreateRow(5 + i, "REC" + i, "Original row " + i))).ToArray();
            await db.BulkInsertStringsAsync(rows, CancellationToken.None);
            var first = (await db.GetStringsAsync(1, 0, CancellationToken.None)).Single();
            await db.UpdateStringTranslationAsync(first.Id, "번역문은 원문 샘플이 아닙니다", StringEntryStatus.Edited, null, CancellationToken.None);

            var report = await new ProjectContextScanner().ScanAsync(db, null,
                new ProjectContextScanOptions("Plain.esp", "Plain.esp", "english", "korean"), CancellationToken.None);

            Assert.Equal(8, report.Samples.Count);
            Assert.All(report.Samples.GroupBy(sample => sample.Rec), group => Assert.InRange(group.Count(), 1, 2));
            Assert.Single(report.Samples.Where(sample => sample.Text == "Iron Sword"));
            Assert.Contains(report.Samples, sample => sample.Text == "Iron Mace");
            Assert.DoesNotContain(report.Samples, sample => sample.Text == "Iron Dagger");
            Assert.DoesNotContain(report.Samples, sample => sample.Text.Contains("번역문", StringComparison.Ordinal));
            Assert.All(report.Samples, sample => Assert.InRange(sample.Text.Length, 1, 221));
            Assert.Equal(new string('x', 220) + "…", report.Samples.Single(sample => sample.Rec == "BOOK:DESC").Text);
        }
        finally
        {
            TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(path);
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
                ModelName: "gemini-3.0-flash-preview",
                BasePromptText: "base",
                CustomPromptText: null,
                UseCustomPrompt: false,
                CreatedAt: now,
                UpdatedAt: now
            ),
            CancellationToken.None
        );
    }

}
