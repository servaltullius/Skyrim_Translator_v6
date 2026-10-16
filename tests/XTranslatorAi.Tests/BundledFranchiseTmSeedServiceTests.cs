using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmSeedServiceTests
{
    // The seeds are English→Korean, but auto-import used the open project's languages: a Chinese-source first project
    // filed the official memory as chinese→korean, and the stamp kept English projects from ever getting it.
    [Theory]
    [InlineData("bundled-skyrim-tes-franchise-tm.tsv", true)]
    [InlineData("bundled-starfield-franchise-tm.tsv", true)]
    [InlineData("my-own-terms.tsv", false)]
    public void BundledSeeds_AreImportedAsEnglishToKorean(string fileName, bool bundled)
        => Assert.Equal(bundled ? ("english", "korean") : null, BundledFranchiseTmSeedService.LanguagesOfSeedFile(@"C:\tm-import\" + fileName));

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

    [Theory]
    [InlineData(BethesdaFranchise.ElderScrolls)]
    [InlineData(BethesdaFranchise.Fallout)]
    [InlineData(BethesdaFranchise.Starfield)]
    public void EmbeddedSeed_MatchesBundledMetadataBytes(BethesdaFranchise franchise)
    {
        // A CRLF checkout (core.autocrlf=true) would embed different bytes; the on-disk
        // seed would then never validate and be rewritten on every project load.
        var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(franchise)!;
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(EmbeddedAssets.LoadBundledFranchiseTmSeed(franchise)!);

        Assert.Equal(metadata.ExpectedByteLength, bytes.LongLength);
        Assert.Equal(metadata.ExpectedSha256, Convert.ToHexString(SHA256.HashData(bytes)), ignoreCase: true);
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
    public async Task EnsureBundledSeedAsync_DoesNotRewriteSeedMovedByAutoImport()
    {
        var root = CreateTempRoot();
        try
        {
            var loadCalls = 0;
            var service = new BundledFranchiseTmSeedService(root, franchise =>
            {
                loadCalls++;
                return EmbeddedAssets.LoadBundledFranchiseTmSeed(franchise);
            });
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);

            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Starfield)!;
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Starfield, root);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            Assert.True(File.Exists(seedPath));

            // Mirrors TryAutoImportFranchiseTranslationMemoryAsync after a successful import.
            var importedDir = Path.Combine(importDir, "imported");
            Directory.CreateDirectory(importedDir);
            File.Move(seedPath, Path.Combine(importedDir, "bundled-starfield-franchise-tm.imported.20260930-000000.tsv"));

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Starfield, CancellationToken.None);

            Assert.Equal(1, loadCalls);
            Assert.False(File.Exists(seedPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task EnsureBundledSeedAsync_RewritesMissingSeedWhenStampIsForDifferentContent()
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Fallout)!;
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, metadata.Version, root);
            await File.WriteAllTextAsync(
                stampPath,
                string.Join("|", metadata.Version, metadata.ExpectedByteLength, new string('0', 64), "0"),
                CancellationToken.None
            );

            var service = new BundledFranchiseTmSeedService(root);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

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
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.ElderScrolls)!.Version, root);
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

            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.ElderScrolls)!.Version, root);
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

    // v1 kept a minority spelling where the Korean patch translates one source several ways ("Ice Storm" is 얼음 폭풍
    // in 27 strings and 얼음의 형상 in 2); v3 to v6 fix patch errors reviews found ("머서 프레이와와 대화하기", "화살 -
    // 강철 화살", "빛나는 끌개", "강철 문"). Users who already imported an earlier seed get only the rows changed since
    // their version, so other rows they edited in the global memory stay; a new install imports the whole seed.
    [Theory]
    [InlineData("v5|1528360|1aa05d8bb8f743812f7870aea4a5b444028fc07647b2cdb71ecedbde3371477d|0", false, false, false, false)]
    [InlineData("v4|1527387|20d5bd50e2d722876bc0066bb627d446bc6abda67a88760f03ec6a91e490f426|0", true, false, false, false)]
    [InlineData("v3|1527327|e66600f3aff94bd6a9f7bf2f006279580d8b52aa74734640b3bec9b4a809d2eb|0", true, true, false, false)]
    [InlineData("v2|1527256|3a72a1830ab346b43fa3fb97251738d00480224ba014c3ff59005c47b5efb0fd|0", true, true, true, false)]
    [InlineData("v1|1527384|8fd8931be7c1e7da90d99cbaaeac001ec4809c170fd18feb4274ceac83e69a44|0", true, true, true, true)]
    public async Task ElderScrollsSeed_V6_ImportsOnlyTheChangesSinceTheImportedVersion(string stamp, bool beforeV5, bool beforeV4, bool beforeV3, bool fromV1)
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.ElderScrolls)!;
            Assert.Equal("v6", metadata.Version);
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
            Directory.CreateDirectory(importDir);
            var previousStamp = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, stamp[..2], root);
            await File.WriteAllTextAsync(previousStamp, stamp, CancellationToken.None);

            var fullLoads = 0;
            var service = new BundledFranchiseTmSeedService(root, franchise =>
            {
                fullLoads++;
                return EmbeddedAssets.LoadBundledFranchiseTmSeed(franchise);
            });
            await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

            Assert.Equal(0, fullLoads);
            Assert.False(File.Exists(Path.Combine(importDir, metadata.FileName)));
            var changesPath = Path.Combine(importDir, metadata.ChangesFileName!);
            var changes = await File.ReadAllTextAsync(changesPath, CancellationToken.None);
            Assert.StartsWith("Source\tTarget\tPrevious\n", changes, StringComparison.Ordinal);
            // The second review, of names and descriptions, is in every upgrade: Iron is 철, not the 강철 of Steel;
            // a weakness poison takes the effect's 약점; Vignar is 비그나 as his lines say.
            Assert.Contains("\nIron Door\t철문\t강철 문\n", changes, StringComparison.Ordinal);
            Assert.Contains("\nWeak Aversion to Magic\t독 - 마법 약점 하급\t독 - 마법 저항 하급\n", changes, StringComparison.Ordinal);
            Assert.Contains("\nVignar\t비그나\t비그너\n", changes, StringComparison.Ordinal);
            // The rounds before it reach only those who imported a seed older than v5.
            Assert.Equal(beforeV5, changes.Contains("\nLightning Attractor\t번개 유도장치\t빛나는 끌개\n", StringComparison.Ordinal));
            // Decisions after the review: Jumping spiders were named 폭발형 like the exploding ones.
            Assert.Equal(beforeV5, changes.Contains("\nJumping Frost Spider\t도약형 냉기 거미\t폭발형 냉기 거미\n", StringComparison.Ordinal));
            Assert.Equal(beforeV5, changes.Contains("\nPacify\t광역 진정\t평온\n", StringComparison.Ordinal));
            Assert.Equal(beforeV5, changes.Contains("\nGlowing Spider\t발광 거미\t빛나는 거미\n", StringComparison.Ordinal));
            // The third review round, of descriptions.
            Assert.Equal(beforeV5, changes.Contains("\nDrains <mag> points from Carry Weight.\t무게 한계가 <mag> 감소합니다.\t무게 한계를 <mag> 흡수합니다.\n", StringComparison.Ordinal));
            Assert.Equal(beforeV5, changes.Contains("\nEnchanter's Tower, Doors\t마법부여 연구탑, 문\t마법부가 연구탑,문들\n", StringComparison.Ordinal));
            // The fourth review round, of quests.
            Assert.Equal(beforeV5, changes.Contains("\nPay Tolfidr 1000 gold to lift your suspension\t정학 해제를 위해 톨프딜에게 1000골드 지불하기\t정학 해제를 위해 톨프딜에게 100골드 지불하기\n", StringComparison.Ordinal));
            // The decisions after it: the Augur is a man, 예언자, and the Oil Spider recipe names the spider as it is.
            Assert.Equal(beforeV5, changes.Contains("\nAugur of Dunlain\t던레인의 예언자\t던레인의 예언\n", StringComparison.Ordinal));
            // The sixth round, of the player's dialogue choices.
            Assert.Equal(beforeV5, changes.Contains("\nI don't need your advice.\t당신의 조언은 필요 없소.\t당신의 조언이 필요한데.\n", StringComparison.Ordinal));
            // Gallows Hall, the Creation Club home, no longer shares 교수대 바위 with Gallows Rock.
            Assert.Equal(beforeV5, changes.Contains("\nGallows Hall\t교수대 회관\t교수대 바위\n", StringComparison.Ordinal));
            // The last batch, of game settings text: Nose Length was 코 높이.
            Assert.Equal(beforeV5, changes.Contains("\nNose Length\t코 길이\t코 높이\n", StringComparison.Ordinal));
            // The decisions after it: the Companions take the patch's spelling.
            Assert.Equal(beforeV5, changes.Contains("\nWho are the Companions?\t컴패니언이 누구요?\t컴패니언즈가 누구요?\n", StringComparison.Ordinal));
            // The gallery's typo reports the review took.
            Assert.Equal(beforeV5, changes.Contains("\nKvenel the Tongue\t혀 크베넬\t언어의 크베넬\n", StringComparison.Ordinal));
            // Majni takes the spelling of his eight lines.
            Assert.Equal(beforeV5, changes.Contains("\nMajni\t마지니\t매즈니\n", StringComparison.Ordinal));
            // Goldenglow Estate is an apiary on its own island, a farm rather than a district.
            Assert.Equal(beforeV5, changes.Contains("\nGoldenglow Estate\t골든글로우 농장\t골든글로우 지구\n", StringComparison.Ordinal));
            Assert.DoesNotContain("\nExploding Spider\t", changes, StringComparison.Ordinal);
            Assert.Equal(beforeV4, changes.Contains("\nSuvaris Atheron's Logbook\t수바리스 아테론의 항해일지\t수바리스 아테론의 항박일지\n", StringComparison.Ordinal));
            // v1 had 화살 - 강철; v2 took the 화살 - 강철 화살 of the records the game does not show, and v4 goes back.
            Assert.Equal(beforeV4 && !fromV1, changes.Contains("\nSteel Arrow\t화살 - 강철\t화살 - 강철 화살\n", StringComparison.Ordinal));
            Assert.Equal(beforeV3, changes.Contains("\nSpeak to Mercer Frey\t머서 프레이와 대화하기\t머서 프레이와와 대화하기\n", StringComparison.Ordinal));
            Assert.Equal(fromV1, changes.Contains("\nIce Storm\t얼음 폭풍\t얼음의 형상\n", StringComparison.Ordinal));
            Assert.Equal(("english", "korean"), BundledFranchiseTmSeedService.LanguagesOfSeedFile(changesPath));

            // Auto-import moves the file away; the next load does nothing.
            File.Delete(changesPath);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
            Assert.False(File.Exists(changesPath));
            Assert.False(File.Exists(Path.Combine(importDir, metadata.FileName)));
            Assert.Equal(0, fullLoads);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ElderScrollsSeed_V5_NewInstallGetsTheWholeSeed()
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.ElderScrolls)!;
            await new BundledFranchiseTmSeedService(root).EnsureBundledSeedAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
            var seed = await File.ReadAllTextAsync(Path.Combine(importDir, metadata.FileName), CancellationToken.None);
            Assert.Contains("\nIce Storm\t얼음 폭풍\n", seed, StringComparison.Ordinal);
            Assert.Contains("\nSpeak to Mercer Frey\t머서 프레이와 대화하기\n", seed, StringComparison.Ordinal);
            Assert.Contains("\nSteel Arrow\t화살 - 강철\n", seed, StringComparison.Ordinal);
            Assert.Contains("\nLightning Attractor\t번개 유도장치\n", seed, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(importDir, metadata.ChangesFileName!)));
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
