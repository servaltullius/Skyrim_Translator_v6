using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmSeedServiceTests
{
    [Fact]
    public void LoadBundledFranchiseTmSeed_Fallout_ReturnsEmbeddedSeedText()
    {
        var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Fallout);

        Assert.NotNull(seed);
        Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
        Assert.Contains("Pip-Boy", seed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSeedAsync_Fallout_WritesVersionedSeedOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bundled-seed-{Guid.NewGuid():N}");
        try
        {
            var sut = new BundledFranchiseTmSeedService(root);
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);

            await sut.EnsureSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);
            await sut.EnsureSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            var tsvFiles = Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly);
            Assert.Single(tsvFiles);
            Assert.Contains(BundledFranchiseTmSeedService.FalloutSeedVersion, Path.GetFileName(tsvFiles[0]), StringComparison.Ordinal);

            var seedText = await File.ReadAllTextAsync(tsvFiles[0], CancellationToken.None);
            Assert.Contains("Pip-Boy", seedText, StringComparison.Ordinal);

            var stampFiles = Directory.GetFiles(importDir, "*.stamp", SearchOption.TopDirectoryOnly);
            Assert.Single(stampFiles);
            Assert.Contains(BundledFranchiseTmSeedService.FalloutSeedVersion, Path.GetFileName(stampFiles[0]), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnsureSeedAsync_Fallout_WithExistingStamp_DoesNotInjectAgain()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bundled-seed-{Guid.NewGuid():N}");
        try
        {
            var sut = new BundledFranchiseTmSeedService(root);
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var stampPath = Path.Combine(
                importDir,
                $".bundled-franchise-tm-seed.{BundledFranchiseTmSeedService.FalloutSeedVersion}.stamp"
            );
            await File.WriteAllTextAsync(stampPath, "already-seeded", CancellationToken.None);

            await sut.EnsureSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            Assert.Empty(Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly));
            Assert.Single(Directory.GetFiles(importDir, "*.stamp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnsureSeedAsync_NonFallout_DoesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bundled-seed-{Guid.NewGuid():N}");
        try
        {
            var sut = new BundledFranchiseTmSeedService(root);
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Starfield, root);

            await sut.EnsureSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);

            Assert.Empty(Directory.GetFiles(importDir, "*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
