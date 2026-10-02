using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text.ProjectContext;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The generated project context listed "Deathblow: 치명타" with nothing to support it, and the translation
/// followed that over the earlier patch's 치명적 일격. The scan now carries the earlier translations as evidence.
/// </summary>
public class ProjectContextPreviousTranslationTests
{
    [Fact]
    public async Task TermsWithoutTarget_CarryTheirEarlierTranslationsAsEvidence()
    {
        var report = await ScanAsync(n => $"Deathblow Strike {n}", n => $"치명적 일격 타격 {n}");

        var deathblow = Assert.Single(report.TopTerms, t => string.Equals(t.Source, "Deathblow", StringComparison.OrdinalIgnoreCase));
        Assert.Null(deathblow.Target);
        Assert.Equal(new[] { "Deathblow Strike 1 => 치명적 일격 타격 1", "Deathblow Strike 2 => 치명적 일격 타격 2" },
            deathblow.PreviousTranslations);
    }

    [Fact]
    public async Task Examples_UseTheUsualTranslation_NotTheFirstRowsFound()
    {
        // Like the Elden Rim patch: "Annotation" is mostly 주석, but the first row found says 어노테이션.
        var report = await ScanAsync(n => $"Annotation - Skill {n}", n => n == 1 ? $"어노테이션 - 기술 {n}" : $"주석 - 기술 {n}");

        var annotation = Assert.Single(report.TopTerms, t => string.Equals(t.Source, "Annotation", StringComparison.OrdinalIgnoreCase));
        Assert.All(annotation.PreviousTranslations!, example => Assert.Contains("주석", example));
    }

    private static async Task<ProjectContextScanReport> ScanAsync(Func<int, string> source, Func<int, string> previous)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"xt-ctx-{Guid.NewGuid():N}.sqlite");
        var pluginPath = Path.Combine(Path.GetTempPath(), $"xt-ctx-{Guid.NewGuid():N}.esp");
        await File.WriteAllBytesAsync(pluginPath, PluginProjectIntegrationTests.CreateMinimalPlugin());
        var db = await ProjectDb.OpenOrCreateAsync(dbPath, CancellationToken.None);
        try
        {
            var info = (await PluginReader.ReadAsync(pluginPath, new PluginReadOptions(), CancellationToken.None)).Info;
            var fields = Enumerable.Range(1, 3)
                .Select(n => new PluginField($"MGEF/0500086{n}/0/FULL/0", n - 1, "MGEF", "FULL", 0x05000860u + (uint)n, $"Combo{n}", n, 1,
                    source(n)))
                .ToList();
            var project = new ProjectInfo(1, "", "Test.esp", BethesdaFranchise.ElderScrolls, "english", "korean", "", false,
                "", "model", "", "", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await db.ReplaceImportedPluginStringsAsync(info, fields, project, "utf-8", CancellationToken.None);
            await db.ReplacePreviousTranslationsAsync("Old.esp",
                fields.ToDictionary(f => f.Key, f => previous(f.OrderIndex + 1)), CancellationToken.None);

            return await new ProjectContextScanner().ScanAsync(db, null,
                new ProjectContextScanOptions("Test.esp", "Test.esp", "english", "korean", null), CancellationToken.None);
        }
        finally
        {
            await db.DisposeAsync();
            TestDbHelper.TryDeleteDbFiles(dbPath);
            File.Delete(pluginPath);
        }
    }
}
