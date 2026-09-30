using System;
using System.IO;
using System.Security.Cryptography;
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
            var seedPath = Path.Combine(importDir, metadata.FileName);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(franchise, metadata.Version, _globalRootOverride);

            var seedMatches = await TryValidateOnDiskSeedAsync(seedPath, metadata, cancellationToken);
            if (seedMatches)
            {
                var stampText = File.Exists(stampPath)
                    ? await File.ReadAllTextAsync(stampPath, cancellationToken)
                    : string.Empty;

                if (IsLegacyVersionOnlyStamp(stampText, metadata.Version))
                {
                    await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
                    return;
                }

                if (TryParseBundledSeedStamp(stampText, out var stamp) && BundledSeedStampMatchesMetadata(stamp, metadata))
                {
                    return;
                }

                await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
                return;
            }

            await RewriteSeedAsync(seedPath, stampPath, metadata, cancellationToken);
        }
        catch
        {
            // Best-effort only. Bundled seed copy must never block XML loading.
        }
    }

    public static BundledSeedMetadata? GetBundledSeedMetadata(BethesdaFranchise franchise)
        => franchise switch
        {
            BethesdaFranchise.ElderScrolls => new BundledSeedMetadata(BethesdaFranchise.ElderScrolls, "v1", "bundled-skyrim-tes-franchise-tm.tsv", 1527384, "8fd8931be7c1e7da90d99cbaaeac001ec4809c170fd18feb4274ceac83e69a44"),
            BethesdaFranchise.Fallout => new BundledSeedMetadata(BethesdaFranchise.Fallout, "v1", "bundled-fallout4-franchise-tm.tsv", 76, "1a4c3068961a75f6245b2392b018c91a6debdc68eb09cb7708d21ac6a3f93c2e"),
            BethesdaFranchise.Starfield => new BundledSeedMetadata(BethesdaFranchise.Starfield, "v1", "bundled-starfield-franchise-tm.tsv", 18094989, "6e3335684e3cd169051820e14684cc7a1165c0ac6c0db9fae71e63a46ca7f47a"),
            _ => null,
        };

    private async Task RewriteSeedAsync(string seedPath, string stampPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
    {
        var seedText = _loadBundledSeedText(metadata.Franchise);
        if (string.IsNullOrWhiteSpace(seedText))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(seedPath)!);
        await File.WriteAllTextAsync(seedPath, seedText, cancellationToken);
        await WriteStampAsync(stampPath, seedPath, metadata, cancellationToken);
    }

    private static async Task<bool> TryValidateOnDiskSeedAsync(string seedPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
    {
        if (!File.Exists(seedPath))
        {
            return false;
        }

        var seedInfo = new FileInfo(seedPath);
        if (seedInfo.Length != metadata.ExpectedByteLength)
        {
            return false;
        }

        var onDiskSha256 = await ComputeSha256HexAsync(seedPath, cancellationToken);
        return string.Equals(onDiskSha256, metadata.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyVersionOnlyStamp(string stampText, string expectedVersion)
        => string.Equals(stampText.Trim(), expectedVersion, StringComparison.Ordinal);

    private static bool TryParseBundledSeedStamp(string stampText, out BundledSeedStamp stamp)
    {
        stamp = default!;
        var parts = stampText.Trim().Split('|');
        if (parts.Length != 4)
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var expectedByteLength))
        {
            return false;
        }

        if (!long.TryParse(parts[3], out var lastWriteUtcTicks))
        {
            return false;
        }

        stamp = new BundledSeedStamp(parts[0], expectedByteLength, parts[2], lastWriteUtcTicks);
        return true;
    }

    private static bool BundledSeedStampMatchesMetadata(BundledSeedStamp stamp, BundledSeedMetadata metadata)
        => string.Equals(stamp.Version, metadata.Version, StringComparison.Ordinal)
            && stamp.ExpectedByteLength == metadata.ExpectedByteLength
            && string.Equals(stamp.ExpectedSha256, metadata.ExpectedSha256, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Task WriteStampAsync(string stampPath, string seedPath, BundledSeedMetadata metadata, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(stampPath, BuildBundledSeedStamp(metadata, File.GetLastWriteTimeUtc(seedPath).Ticks), cancellationToken);

    private static string BuildBundledSeedStamp(BundledSeedMetadata metadata, long lastWriteUtcTicks)
        => string.Join("|", metadata.Version, metadata.ExpectedByteLength, metadata.ExpectedSha256, lastWriteUtcTicks);

    public sealed record BundledSeedMetadata(BethesdaFranchise Franchise, string Version, string FileName, long ExpectedByteLength, string ExpectedSha256);

    private sealed record BundledSeedStamp(string Version, long ExpectedByteLength, string ExpectedSha256, long LastWriteUtcTicks);
}
