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
    public void LoadBundledFranchiseTmSeed_ElderScrolls_ReturnsEmbeddedSeedText()
    {
        var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.ElderScrolls);

        Assert.NotNull(seed);
        Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
        Assert.Contains("Stormcloaks", seed, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadBundledFranchiseTmSeed_Starfield_ReturnsEmbeddedSeedText()
    {
        var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Starfield);

        Assert.NotNull(seed);
        Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
        Assert.Contains("All Must Serve", seed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_SkipsEmbeddedLoadWhenStampedSeedIsCurrent()
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Fallout)!;
            var seedText = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Fallout)!;

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, metadata.Version, root);

            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(seedPath, seedText, CancellationToken.None);
            await File.WriteAllTextAsync(stampPath, BuildCurrentStamp(metadata), CancellationToken.None);

            var loadCalls = 0;
            var service = new BundledFranchiseTmSeedService(root, _ =>
            {
                loadCalls++;
                throw new InvalidOperationException("Embedded seed should not be loaded on the fast path.");
            });

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            Assert.Equal(0, loadCalls);
            Assert.Equal(metadata.ExpectedByteLength, new FileInfo(seedPath).Length);
            AssertCurrentStamp(await File.ReadAllTextAsync(stampPath, CancellationToken.None), metadata);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_WritesStampWithoutLoadingWhenSeedIsAlreadyCurrent()
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Fallout)!;
            var seedText = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Fallout)!;

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, metadata.Version, root);

            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(seedPath, seedText, CancellationToken.None);

            var loadCalls = 0;
            var service = new BundledFranchiseTmSeedService(root, _ =>
            {
                loadCalls++;
                throw new InvalidOperationException("Embedded seed should not be loaded when the on-disk file is already current.");
            });

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            Assert.Equal(0, loadCalls);
            Assert.True(File.Exists(stampPath));
            AssertCurrentStamp(await File.ReadAllTextAsync(stampPath, CancellationToken.None), metadata);
            Assert.Equal(metadata.ExpectedByteLength, new FileInfo(seedPath).Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_RewritesSameSizeCorruptedSeedWhenStampedMetadataLooksValid()
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Fallout)!;
            var originalSeedText = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Fallout)!;

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, metadata.Version, root);

            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(seedPath, originalSeedText, CancellationToken.None);
            await File.WriteAllTextAsync(stampPath, BuildCurrentStamp(metadata), CancellationToken.None);

            var corruptedSeedText = MakeSameLengthCorruption(originalSeedText);
            await File.WriteAllTextAsync(seedPath, corruptedSeedText, CancellationToken.None);

            var loadCalls = 0;
            var service = new BundledFranchiseTmSeedService(root, _ =>
            {
                loadCalls++;
                return originalSeedText;
            });

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            Assert.Equal(1, loadCalls);
            Assert.Equal(originalSeedText, await File.ReadAllTextAsync(seedPath, CancellationToken.None));
            AssertCurrentStamp(await File.ReadAllTextAsync(stampPath, CancellationToken.None), metadata);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_UpgradesLegacyVersionOnlyStampWithoutLoadingWhenSeedMatchesHash()
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Fallout)!;
            var seedText = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.Fallout)!;

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, metadata.Version, root);

            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(seedPath, seedText, CancellationToken.None);
            await File.WriteAllTextAsync(stampPath, metadata.Version, CancellationToken.None);

            var loadCalls = 0;
            var service = new BundledFranchiseTmSeedService(root, _ =>
            {
                loadCalls++;
                throw new InvalidOperationException("Embedded seed should not be loaded when upgrading a legacy stamp on a matching file.");
            });

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            Assert.Equal(0, loadCalls);
            AssertCurrentStamp(await File.ReadAllTextAsync(stampPath, CancellationToken.None), metadata);
            Assert.Equal(seedText, await File.ReadAllTextAsync(seedPath, CancellationToken.None));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
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
    public async Task EnsureBundledSeedAsync_WritesSeedOnceForElderScrolls()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new BundledFranchiseTmSeedService(root);

            await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, "v1", root);
            var files = Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly);

            Assert.Single(files);
            Assert.Equal("bundled-skyrim-tes-franchise-tm.tsv", Path.GetFileName(files[0]));
            Assert.True(File.Exists(stampPath));
            var seed = await File.ReadAllTextAsync(files[0], CancellationToken.None);
            Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
            Assert.Contains("Stormcloaks", seed, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_WritesSeedOnceForStarfield()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new BundledFranchiseTmSeedService(root);

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Starfield, root);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Starfield, "v1", root);
            var files = Directory.GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly);

            Assert.Single(files);
            Assert.Equal("bundled-starfield-franchise-tm.tsv", Path.GetFileName(files[0]));
            Assert.True(File.Exists(stampPath));
            var seed = await File.ReadAllTextAsync(files[0], CancellationToken.None);
            Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
            Assert.Contains("All Must Serve", seed, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_DoesNothingForUnsupportedFranchise()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new BundledFranchiseTmSeedService(root);

            await service.EnsureBundledSeedAsync((BethesdaFranchise)999, CancellationToken.None);

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir((BethesdaFranchise)999, root);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath((BethesdaFranchise)999, "v1", root);

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
    public async Task EnsureBundledSeedAsync_RepairsPreexistingStaleSeedForElderScrolls()
    {
        var root = CreateTempRoot();
        try
        {
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
            var seedPath = Path.Combine(importDir, "bundled-skyrim-tes-franchise-tm.tsv");
            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(seedPath, "Source\tTarget\nStormcloaks\tWRONG", CancellationToken.None);

            var service = new BundledFranchiseTmSeedService(root);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, "v1", root);
            var seed = await File.ReadAllTextAsync(seedPath, CancellationToken.None);

            Assert.True(File.Exists(stampPath));
            Assert.DoesNotContain("WRONG", seed, StringComparison.Ordinal);
            Assert.Contains("Stormcloaks", seed, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_RepairsPreexistingStaleSeedForStarfield()
    {
        var root = CreateTempRoot();
        try
        {
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Starfield, root);
            var seedPath = Path.Combine(importDir, "bundled-starfield-franchise-tm.tsv");
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Starfield, "v1", root);
            Directory.CreateDirectory(importDir);

            await File.WriteAllTextAsync(seedPath, "Source\tTarget\nAll Must Serve\tWRONG", CancellationToken.None);
            await File.WriteAllTextAsync(stampPath, "v1", CancellationToken.None);

            var service = new BundledFranchiseTmSeedService(root);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);

            var seed = await File.ReadAllTextAsync(seedPath, CancellationToken.None);

            Assert.True(File.Exists(stampPath));
            Assert.DoesNotContain("WRONG", seed, StringComparison.Ordinal);
            Assert.Contains("All Must Serve", seed, StringComparison.Ordinal);
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

    private static string BuildCurrentStamp(BundledFranchiseTmSeedService.BundledSeedMetadata metadata)
        => string.Join("|", metadata.Version, metadata.ExpectedByteLength, metadata.ExpectedSha256, "0");

    private static void AssertCurrentStamp(string stampText, BundledFranchiseTmSeedService.BundledSeedMetadata metadata)
    {
        var parts = stampText.Trim().Split('|');
        Assert.Equal(4, parts.Length);
        Assert.Equal(metadata.Version, parts[0]);
        Assert.Equal(metadata.ExpectedByteLength.ToString(), parts[1]);
        Assert.Equal(metadata.ExpectedSha256, parts[2], ignoreCase: true);
        Assert.True(long.TryParse(parts[3], out _));
    }

    private static string MakeSameLengthCorruption(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "x";
        }

        var last = text[^1] == 'x' ? 'y' : 'x';
        return text[..^1] + last;
    }
}
