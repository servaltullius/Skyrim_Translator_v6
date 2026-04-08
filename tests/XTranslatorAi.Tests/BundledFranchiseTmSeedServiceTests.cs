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
    public async Task EnsureBundledSeedAsync_WritesSeedOnceForFallout()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new BundledFranchiseTmSeedService(root);

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, "v1", root);
            var files = Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly);

            Assert.Single(files);
            Assert.Equal("bundled-fallout4-franchise-tm.tsv", Path.GetFileName(files[0]));
            Assert.True(File.Exists(stampPath));
            var seed = await File.ReadAllTextAsync(files[0], CancellationToken.None);
            Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_DoesNothingForNonFalloutFranchise()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new BundledFranchiseTmSeedService(root);

            await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, "v1", root);

            Assert.Empty(Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly));
            Assert.False(File.Exists(stampPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_ReplacesPreexistingStaleSeedBeforeStamping()
    {
        var root = CreateTempRoot();
        try
        {
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, "bundled-fallout4-franchise-tm.tsv");
            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(seedPath, "Source\tTarget\nPip-Boy\tWRONG", CancellationToken.None);

            var service = new BundledFranchiseTmSeedService(root);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, "v1", root);
            var seed = await File.ReadAllTextAsync(seedPath, CancellationToken.None);

            Assert.True(File.Exists(stampPath));
            Assert.DoesNotContain("WRONG", seed, StringComparison.Ordinal);
            Assert.Contains("Pip-Boy", seed, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_RepairsExistingSeedEvenWhenStampAlreadyExists()
    {
        var root = CreateTempRoot();
        try
        {
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, "bundled-fallout4-franchise-tm.tsv");
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, "v1", root);
            Directory.CreateDirectory(importDir);

            await File.WriteAllTextAsync(seedPath, "Source\tTarget\nPip-Boy\tWRONG", CancellationToken.None);
            await File.WriteAllTextAsync(stampPath, "v1", CancellationToken.None);

            var service = new BundledFranchiseTmSeedService(root);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            var seed = await File.ReadAllTextAsync(seedPath, CancellationToken.None);

            Assert.True(File.Exists(stampPath));
            Assert.DoesNotContain("WRONG", seed, StringComparison.Ordinal);
            Assert.Contains("Pip-Boy", seed, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "xtai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
