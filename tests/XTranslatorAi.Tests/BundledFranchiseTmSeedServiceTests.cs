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
    // Any version's changes file: an earlier one left in tm-import when the next version wrote its own.
    [InlineData("bundled-skyrim-tes-franchise-tm-v12-changes.tsv", true)]
    [InlineData("bundled-skyrim-tes-franchise-tm-v11-changes.tsv", true)]
    [InlineData("bundled-skyrim-tes-franchise-tm-v10-changes.tsv", true)]
    [InlineData("bundled-skyrim-tes-franchise-tm-v9-changes.tsv", true)]
    [InlineData("bundled-skyrim-tes-franchise-tm-v8-changes.tsv", true)]
    [InlineData("bundled-skyrim-tes-franchise-tm-v7-changes.tsv", true)]
    [InlineData("bundled-skyrim-tes-franchise-tm-v-changes.tsv", false)]
    [InlineData("bundled-skyrim-tes-franchise-tm-mine-changes.tsv", false)]
    [InlineData("my-own-terms.tsv", false)]
    public void BundledSeeds_AreImportedAsEnglishToKorean(string fileName, bool bundled)
        => Assert.Equal(bundled ? ("english", "korean") : null, BundledFranchiseTmSeedService.LanguagesOfSeedFile(@"C:\tm-import\" + fileName));

    // A changes file left waiting (its import failed) is read before the next version's: by name alone v10 came before v9,
    // and a row both versions changed kept v9's text, as v10's Previous no longer matched.
    [Fact]
    public void WaitingFiles_AreImportedInVersionOrder()
        => Assert.Equal(
            new[] { @"C:\tm-import\bundled-skyrim-tes-franchise-tm-v9-changes.tsv", @"C:\tm-import\bundled-skyrim-tes-franchise-tm-v10-changes.tsv", @"C:\tm-import\my-terms.tsv" },
            FranchiseTranslationMemoryService.InImportOrder(new[] { @"C:\tm-import\my-terms.tsv", @"C:\tm-import\bundled-skyrim-tes-franchise-tm-v10-changes.tsv", @"C:\tm-import\bundled-skyrim-tes-franchise-tm-v9-changes.tsv" }));

    // The import itself: a v9 changes file still waiting and the v10 one, both changing one row, end in v10's text.
    [Fact]
    public async Task WaitingChangesFiles_OfTwoVersions_EndInTheLaterText()
    {
        var root = CreateTempRoot();
        var global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(root, "global"));
        try
        {
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, Path.Combine(root, "global"));
            Directory.CreateDirectory(importDir);
            await File.WriteAllTextAsync(Path.Combine(importDir, "a-memory.tsv"), "Source\tTarget\nWaiting row\t옛 글\n");
            await File.WriteAllTextAsync(Path.Combine(importDir, "bundled-skyrim-tes-franchise-tm-v9-changes.tsv"), "Source\tTarget\tPrevious\nWaiting row\tv9 글\t옛 글\n");
            await File.WriteAllTextAsync(Path.Combine(importDir, "bundled-skyrim-tes-franchise-tm-v10-changes.tsv"), "Source\tTarget\tPrevious\nWaiting row\tv10 글\tv9 글\n");

            var tm = new FranchiseTranslationMemoryService(global);
            var (_, unreadable) = await tm.ImportPendingFilesAsync("english", "korean", null, CancellationToken.None);

            Assert.Null(unreadable);
            Assert.Contains(await tm.GetEntriesAsync("english", "korean", CancellationToken.None), e => e.SourceText == "Waiting row" && e.DestText == "v10 글");
        }
        finally
        {
            await global.DisposeAsync();
            foreach (var db in Directory.GetFiles(root, "*.sqlite", SearchOption.AllDirectories))
            {
                XTranslatorAi.Tests.TestSupport.TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
            }

            TryDeleteDirectory(root);
        }
    }

    // Stopped while it was written (an open the user stopped), the seed was swallowed half written and imported as it was.
    [Fact]
    public async Task EnsureBundledSeedAsync_Stopped_LeavesNoSeedAndSaysSo()
    {
        var root = Path.Combine(Path.GetTempPath(), "XTranslatorAi.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BundledFranchiseTmSeedService(root, _ => "Source\tTarget\nIron Sword\t철검\n");
            using var stopped = new CancellationTokenSource();
            stopped.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, stopped.Token));

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root);
            Assert.True(!Directory.Exists(importDir) || Directory.GetFiles(importDir).Length == 0);

            // A seed that cannot be put in place (held by another program) is left as it was, with nothing half
            // written beside it, and the failure is not thrown at the open.
            Directory.CreateDirectory(importDir);
            var seedPath = Path.Combine(importDir, BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.Fallout)!.FileName);
            await File.WriteAllTextAsync(seedPath, "old");
            using (new FileStream(seedPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);
            }

            Assert.Equal(new[] { seedPath }, Directory.GetFiles(importDir));
            Assert.Equal("old", await File.ReadAllTextAsync(seedPath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

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

    // The stamp sat in tm-import, which fills with imported/ copies: clearing it took the stamp, and the whole seed was
    // imported again over the rows the user had edited. It is moved next to the global DB.
    [Fact]
    public async Task StampInTmImport_MovesNextToTheGlobalDb_AndSurvivesClearingTmImport()
    {
        var root = CreateTempRoot();
        try
        {
            var legacy = ProjectPaths.GetLegacyBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, "v1", root);
            var service = new BundledFranchiseTmSeedService(root);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.Fallout, "v1", root);
            var stamp = await File.ReadAllTextAsync(stampPath);
            File.Move(stampPath, legacy);

            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);

            Assert.False(File.Exists(legacy));
            Assert.Equal(stamp, await File.ReadAllTextAsync(stampPath));
            Assert.NotEqual(Path.GetDirectoryName(legacy), Path.GetDirectoryName(stampPath));

            // The seed was imported (moved away) and tm-import cleared: nothing is written again.
            Directory.Delete(ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root), recursive: true);
            await service.EnsureBundledSeedAsync(BethesdaFranchise.Fallout, CancellationToken.None);
            Assert.Empty(Directory.GetFiles(ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.Fallout, root), "*.tsv"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
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
    // in 27 strings and 얼음의 형상 in 2); v3 to v8 fix patch errors reviews found ("머서 프레이와와 대화하기", "화살 -
    // 강철 화살", "빛나는 끌개", "강철 문", "도둑길드", "계약없는 드레모라"), v9 takes the text the game shows where
    // an Update or DLC override replaced the Skyrim.esm one, v10 the patch's chunk K, and v11 and v12 its spacing reviews. Users
    // who already imported an earlier seed get only the rows changed since their version, so other rows they edited in the
    // global memory stay; a new install imports the whole seed. The v6 stamp is 1.15's, the v7 one 1.16's to 1.16.39's,
    // the v8 one 1.16.40's, the v9 one 1.16.41's, the v11 one 1.16.42's; v10 never shipped, but a build of main may have written it.
    [Theory]
    [InlineData("v11|1529133|860951f686ebe215b3f5ca3bd9c617da623d6fcee3a8cb2a7d0ddfb037569bab|0", false, false, false, false, false, false, false, false, false, false)]
    [InlineData("v10|1528918|3448f09b36118aa8d9391df8b1ad9d118472e03b3ba7285e256d2689b1eae29b|0", true, false, false, false, false, false, false, false, false, false)]
    [InlineData("v9|1528886|c2b3d628469fbe98ebf24a7051478854265ca4ef29e08b8106f933c26456100b|0", true, true, false, false, false, false, false, false, false, false)]
    [InlineData("v8|1528768|0b4a21c8a7a09d5f10ff09d2f50ac2001446e9fea713d4279b87ac666cd30a42|0", true, true, true, false, false, false, false, false, false, false)]
    [InlineData("v7|1528774|a020a55d45ed0c1a8331294f057c005b8a35fe091fb890c4b676564883ea5db7|0", true, true, true, true, false, false, false, false, false, false)]
    [InlineData("v6|1528761|ed933491d5eafaf99142407ca1a159f92fdeee2ea7d2af625e13ccadd7d8bccc|0", true, true, true, true, true, false, false, false, false, false)]
    [InlineData("v5|1528360|1aa05d8bb8f743812f7870aea4a5b444028fc07647b2cdb71ecedbde3371477d|0", true, true, true, true, true, true, false, false, false, false)]
    [InlineData("v4|1527387|20d5bd50e2d722876bc0066bb627d446bc6abda67a88760f03ec6a91e490f426|0", true, true, true, true, true, true, true, false, false, false)]
    [InlineData("v3|1527327|e66600f3aff94bd6a9f7bf2f006279580d8b52aa74734640b3bec9b4a809d2eb|0", true, true, true, true, true, true, true, true, false, false)]
    [InlineData("v2|1527256|3a72a1830ab346b43fa3fb97251738d00480224ba014c3ff59005c47b5efb0fd|0", true, true, true, true, true, true, true, true, true, false)]
    [InlineData("v1|1527384|8fd8931be7c1e7da90d99cbaaeac001ec4809c170fd18feb4274ceac83e69a44|0", true, true, true, true, true, true, true, true, true, true)]
    public async Task ElderScrollsSeed_V12_ImportsOnlyTheChangesSinceTheImportedVersion(string stamp, bool beforeV11, bool beforeV10, bool beforeV9, bool beforeV8, bool beforeV7, bool beforeV6, bool beforeV5, bool beforeV4, bool beforeV3, bool fromV1)
    {
        var root = CreateTempRoot();
        try
        {
            var metadata = BundledFranchiseTmSeedService.GetBundledSeedMetadata(BethesdaFranchise.ElderScrolls)!;
            Assert.Equal("v12", metadata.Version);
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
            Directory.CreateDirectory(importDir);
            var previousStamp = ProjectPaths.GetBundledFranchiseTmSeedStampPath(BethesdaFranchise.ElderScrolls, stamp.Split('|')[0], root);
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
            // The import reads every row of it, a row new to the seed (no Previous) too.
            var parsed = TranslationMemoryFileService.ParseTsvChanges(await ImportTextFileReader.ReadAllLinesAsync(changesPath, CancellationToken.None));
            Assert.Equal(changes.Count(c => c == '\n') - 1, parsed!.Count);
            // v12 is in every upgrade: the patch's second spacing review. From v11 that is all.
            Assert.Contains("\nHow many do you need?\t몇 개나 필요하오?\t몇개나 필요하오?\n", changes, StringComparison.Ordinal);
            Assert.True(beforeV11 || parsed.Count == 89, $"{parsed.Count} rows from v11");
            // v11 (1.16.42) reaches those who imported a seed older than it: the patch's first spacing review.
            Assert.Equal(beforeV11, changes.Contains("\nDo you know Isabelle Rolaine?\t이자벨 로레인을 알고 있소?\t이자벨 로레인을 알고있소?\n", StringComparison.Ordinal));
            Assert.True(!beforeV11 || beforeV10 || parsed.Count == 319, $"{parsed.Count} rows from v10");
            // v10 reaches those who imported a seed older than it: the soldier discharged at the council speaks to the
            // general in 존댓말.
            Assert.Equal(beforeV10, changes.Contains("\nI did what I thought was right.\t옳다고 생각한 일을 했을 뿐입니다.\t옳다고 생각한 일을 한 것 뿐이오.\n", StringComparison.Ordinal));
            Assert.Equal(beforeV10, changes.Contains("\nSorry, sir. I was trying to come to a fair agreement.\t죄송합니다, 장군님. 공정한 합의를 이루려고 했습니다.\t죄송합니다. 저는 공평한 합의를 이루려고 할 뿐입니다.\n", StringComparison.Ordinal));
            Assert.True(!beforeV10 || beforeV9 || parsed.Count == 323, $"{parsed.Count} rows from v9");
            // v9 (1.16.41) reaches those who imported a seed older than it: the text the game shows where an Update or DLC
            // override replaced the Skyrim.esm string, the particle form the decision gives an <Alias> that takes any
            // name, and the player's choices to Septimus in 하오.
            Assert.Equal(beforeV9, changes.Contains("\nFind the shrine of Boethiah\t보에디아의 성소를 찾기\t보에디아의 성지를 찾기\n", StringComparison.Ordinal));
            Assert.Equal(beforeV9, changes.Contains("\nRescue <Alias=Victim> from <Alias=Lair>\t<Alias=Lair>에서 <Alias=Victim>을(를) 구출하기\t<Alias=Lair>에서 <Alias=Victim>를 구출하기\n", StringComparison.Ordinal));
            Assert.Equal(beforeV9, changes.Contains("\nWhere is this \"Blackreach\"?\t'블랙리치'는 어디에 있소?\t'블랙리치'는 어디에 있나?\n", StringComparison.Ordinal));
            Assert.True(!beforeV9 || beforeV8 || parsed.Count == 368, $"{parsed.Count} rows from v8");
            // The patch's third review round (1.16.40's v8) reaches those who imported a seed older than it: names and
            // terms as the game spells them, the player's choices in the voice of their conversation, a space the Korean
            // fonts have where a no-break space was.
            Assert.Equal(beforeV8, changes.Contains("\nBarter\t거래\t", StringComparison.Ordinal));
            Assert.Equal(beforeV8, changes.Contains("\nUnbound Dremora\t속박되지 않은 드레모라\t", StringComparison.Ordinal));
            Assert.Equal(beforeV8, changes.Contains("\nBecome Guild Master for the Thieves Guild\t도둑 길드의 길드 마스터가 되기\t", StringComparison.Ordinal));
            Assert.Equal(beforeV8, changes.Contains("\nWhat are the Ancestral Tombs?\t조상의 무덤들이란게 뭐요?\t", StringComparison.Ordinal));
            // The decisions after it: Jiub, a stranger, hears 하오체; a confirmation box asks ~하겠습니까?.
            Assert.Equal(beforeV8, changes.Contains("\nWhat ended up happening?\t마지막에는 어떻게 되었소?\t", StringComparison.Ordinal));
            Assert.Equal(beforeV8, changes.Contains("\nDo you want to improve this item?\t이 물품을 강화하겠습니까?\t", StringComparison.Ordinal));
            Assert.Equal(beforeV8, changes.Contains("\nI want you to leave Omluag alone.\t옴러그를 내버려두시오.\t", StringComparison.Ordinal));
            Assert.DoesNotContain('\u00A0', changes.Split('\n').Where(line => line.Length > 0).Select(line => line.Split('\t')[1]).SelectMany(target => target));
            // Sea Squall is new in v8: no earlier seed had it, and the ship took the 스퀄 of Strident Squall.
            Assert.Equal(beforeV8, changes.Contains("\nSea Squall\t바다 소나기 호\t\n", StringComparison.Ordinal));
            // From v3 to v7, 18 rows ended in a CR that broke a changes file's line, and the import dropped them: those
            // who came from v1 or v2 still have the v2 text, which every upgrade from before v8 replaces (v8's did).
            Assert.DoesNotContain('\r', changes);
            Assert.Equal(beforeV8, changes.Contains("\nALTERATION\t변화마법\t변화\n", StringComparison.Ordinal));
            Assert.DoesNotContain("\nFinished?\t", changes, StringComparison.Ordinal);
            // Eight of them took in v3 a text the patch no longer has. v8 takes the patch's: where that is the v2 text
            // (Lisbet 리즈벳), only a v3 or later memory changes; where it is neither, both older texts are offered.
            Assert.Equal(beforeV8 && !beforeV3, changes.Contains("\nLisbet\t리즈벳\t리스벳\n", StringComparison.Ordinal));
            Assert.DoesNotContain("\nLisbet\t리즈벳\t리즈벳\n", changes, StringComparison.Ordinal);
            Assert.Equal(beforeV8 && !beforeV3, changes.Contains("\nReplace the beacon\t등불을 제자리에 놓기\t표식을 교체하기\n", StringComparison.Ordinal));
            Assert.Equal(beforeV8, changes.Contains("\nReplace the beacon\t등불을 제자리에 놓기\t등불을 교체하기\n", StringComparison.Ordinal));
            // The gallery's reports the first match missed reach those who imported a seed older than v7: 도둑 길드, as
            // most of the patch spells it.
            Assert.Equal(beforeV7, changes.Contains("\nThieves Guild Fence Chest\t도둑 길드의 장물 보관함\t", StringComparison.Ordinal));
            Assert.Equal(beforeV7, changes.Contains("\nMudcrab Boss (Old Salty)\t거대 진흙게 (올드 살티)\t", StringComparison.Ordinal));
            // The second review, of names and descriptions (1.15's v6), reaches those who imported a seed older than it:
            // Iron is 철, not the 강철 of Steel; a weakness poison takes the effect's 약점; Vignar is 비그나 as his lines say.
            Assert.Equal(beforeV6, changes.Contains("\nIron Door\t철문\t강철 문\n", StringComparison.Ordinal));
            Assert.Equal(beforeV6, changes.Contains("\nWeak Aversion to Magic\t독 - 마법 약점 하급\t독 - 마법 저항 하급\n", StringComparison.Ordinal));
            Assert.Equal(beforeV6, changes.Contains("\nVignar\t비그나\t비그너\n", StringComparison.Ordinal));
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
            Assert.Contains("\nBarter\t거래\n", seed, StringComparison.Ordinal);
            Assert.Contains("\nFind the shrine of Boethiah\t보에디아의 성소를 찾기\n", seed, StringComparison.Ordinal);
            Assert.Contains("\nSea Cave\t바다 동굴\nSea Squall\t바다 소나기 호\n", seed, StringComparison.Ordinal);
            // No Korean of the seed has a no-break space: the Korean font mods have none, and the game shows a box.
            Assert.DoesNotContain(seed.Split('\n').Where(line => line.Length > 0).Select(line => line.Split('\t')[1]), target => target.Contains('\u00A0'));
            Assert.False(File.Exists(Path.Combine(importDir, metadata.ChangesFileName!)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    // The seed says what the game shows: where every record with an English shows one text, the seed has that text.
    // v1 took 31 rows from Skyrim.esm strings that an Update or DLC override replaces with its own copy, and the reviews
    // fixed the copy the game shows (보에디아의 성소), not the string nobody sees (성지), until v9. A patch fix that
    // changes a seed row's text puts the row in skyrim-tes-franchise-tm-pending.tsv, with the game's new text, in the
    // same commit; the next seed version takes the list in, and a release with rows still waiting stops (the release
    // workflow checks it). A source the game shows several ways is the seed's own choice, as decided (Exploding Spider
    // 폭발 거미 names the kind, where the game's two texts name a poison spider of each kind), and is not checked.
    [Fact]
    public void ElderScrollsSeed_HasTheTextTheGameShows()
    {
        using var stream = EmbeddedAssets.OpenBundledGameStrings()!;
        var shown = XTranslatorAi.Core.Plugins.GameStringDictionary.Load(stream).TextsEveryRecordShows();
        var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(BethesdaFranchise.ElderScrolls)!;
        var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TestData", "skyrim-tes-franchise-tm-pending.tsv"));
        Assert.Equal("Source\tTarget", lines.FirstOrDefault()?.TrimStart('\uFEFF'));
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var row = lines[i].Split('\t');
            Assert.True(row.Length == 2 && row[0].Length > 0 && row[1].Trim().Length > 0, $"pending line {i + 1}: {lines[i]}");
            Assert.True(pending.TryAdd(row[0], row[1]), $"pending line {i + 1} repeats {row[0]}");
        }

        string? Shown(string source)
            => XTranslatorAi.Core.Plugins.GameStringDictionary.EnglishKey(source) is { } key && shown.TryGetValue(key, out var game) ? game.Trim() : null;

        // Read as the import reads the seed (an escaped seed or a third column would read the same).
        var rows = TranslationMemoryFileService.ParseTsvPairs(seed.Split('\n'));
        var differ = rows
            .Where(row => Shown(row.SourceText) is { } game && !string.Equals(game, row.DestText.Trim(), StringComparison.Ordinal)
                && !(pending.TryGetValue(row.SourceText, out var next) && string.Equals(game, next.Trim(), StringComparison.Ordinal)))
            .Select(row => $"{row.SourceText} → {row.DestText}")
            .ToList();
        // A waiting row has the text the game shows now, so the next version does not take an outdated one.
        var stale = pending.Where(row => Shown(row.Key) is { } game && !string.Equals(game, row.Value.Trim(), StringComparison.Ordinal))
            .Select(row => $"{row.Key} → {row.Value}")
            .ToList();

        Assert.True(shown.Count > 20000, $"{shown.Count} texts");
        Assert.True(rows.Count > 20000, $"{rows.Count} seed rows");
        Assert.Empty(differ);
        Assert.Empty(stale);
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
