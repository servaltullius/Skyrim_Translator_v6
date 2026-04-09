using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public sealed class BundledFranchiseTmSeedService
{
    private readonly string? _globalRootOverride;
    private readonly Func<BethesdaFranchise, string?> _loadBundledSeedText;

    public BundledFranchiseTmSeedService(string? globalRootOverride = null, Func<BethesdaFranchise, string?>? loadBundledSeedText = null)
    {
        _globalRootOverride = globalRootOverride;
        _loadBundledSeedText = loadBundledSeedText ?? EmbeddedAssets.LoadBundledFranchiseTmSeed;
    }

    public async Task EnsureBundledSeedAsync(BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = GetBundledSeedMetadata(franchise);
            if (metadata is null)
            {
                return;
            }

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(franchise, _globalRootOverride);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(franchise, metadata.Version, _globalRootOverride);
            var seedPath = Path.Combine(importDir, metadata.FileName);
            if (File.Exists(stampPath))
            {
                if (IsSeedFileCurrent(seedPath, metadata.ExpectedByteLength))
                {
                    return;
                }

                await RewriteSeedAsync(seedPath, metadata, cancellationToken);
                return;
            }

            Directory.CreateDirectory(importDir);

            if (IsSeedFileCurrent(seedPath, metadata.ExpectedByteLength))
            {
                await File.WriteAllTextAsync(stampPath, metadata.Version, cancellationToken);
                return;
            }

            await RewriteSeedAsync(seedPath, metadata, cancellationToken);

        }
        catch
        {
            // Best-effort only. Bundled seed copy must never block XML loading.
        }
    }

    public static BundledSeedMetadata? GetBundledSeedMetadata(BethesdaFranchise franchise)
        => franchise switch
        {
            BethesdaFranchise.ElderScrolls => new BundledSeedMetadata(BethesdaFranchise.ElderScrolls, "v1", "bundled-skyrim-tes-franchise-tm.tsv", 1527384),
            BethesdaFranchise.Fallout => new BundledSeedMetadata(BethesdaFranchise.Fallout, "v1", "bundled-fallout4-franchise-tm.tsv", 76),
            BethesdaFranchise.Starfield => new BundledSeedMetadata(BethesdaFranchise.Starfield, "v1", "bundled-starfield-franchise-tm.tsv", 18094989),
            _ => null,
        };

    private async Task RewriteSeedAsync(string seedPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
    {
        var seedText = _loadBundledSeedText(metadata.Franchise);
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
        await File.WriteAllTextAsync(seedPath, seedText, cancellationToken);
        await File.WriteAllTextAsync(
            ProjectPaths.GetBundledFranchiseTmSeedStampPath(metadata.Franchise, metadata.Version, _globalRootOverride),
            metadata.Version,
            cancellationToken
        );
    }

    private static bool IsSeedFileCurrent(string seedPath, long expectedByteLength)
        => File.Exists(seedPath) && new FileInfo(seedPath).Length == expectedByteLength;

    public sealed record BundledSeedMetadata(BethesdaFranchise Franchise, string Version, string FileName, long ExpectedByteLength);
}
