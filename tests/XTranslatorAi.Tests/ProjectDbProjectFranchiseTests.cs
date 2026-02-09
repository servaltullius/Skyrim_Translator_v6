using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectDbProjectFranchiseTests
{
    [Fact]
    public async Task UpsertProjectAsync_PersistsFranchise()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xt-test-{Guid.NewGuid():N}.sqlite");
        try
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(path, CancellationToken.None);

            var now = DateTimeOffset.UtcNow;
            await db.UpsertProjectAsync(
                new ProjectInfo(
                    Id: 1,
                    InputXmlPath: "C:\\dummy.xml",
                    AddonName: "Dummy",
                    Franchise: BethesdaFranchise.Starfield,
                    SourceLang: "english",
                    DestLang: "korean",
                    XmlVersion: "2",
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

            var loaded = await db.TryGetProjectAsync(CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(BethesdaFranchise.Starfield, loaded!.Franchise);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
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

