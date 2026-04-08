using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public sealed class BundledFranchiseTmSeedService
{
    private readonly string? _globalRootOverride;

    public BundledFranchiseTmSeedService(string? globalRootOverride = null)
    {
        _globalRootOverride = globalRootOverride;
    }

    public async Task EnsureBundledSeedAsync(BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        try
        {
            var seedText = EmbeddedAssets.LoadBundledFranchiseTmSeed(franchise);
            var version = GetSeedVersion(franchise);
            if (string.IsNullOrWhiteSpace(seedText) || string.IsNullOrWhiteSpace(version))
            {
                return;
            }

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(franchise, _globalRootOverride);
            var stampPath = ProjectPaths.GetBundledFranchiseTmSeedStampPath(franchise, version, _globalRootOverride);
            var seedPath = Path.Combine(importDir, GetSeedFileName(franchise));
            if (File.Exists(stampPath))
            {
                await RepairExistingSeedIfNeededAsync(seedPath, seedText, cancellationToken);
                return;
            }

            Directory.CreateDirectory(importDir);

            var shouldWriteSeed = true;
            if (File.Exists(seedPath))
            {
                var existingText = await File.ReadAllTextAsync(seedPath, cancellationToken);
                shouldWriteSeed = !string.Equals(existingText, seedText, StringComparison.Ordinal);
            }

            if (shouldWriteSeed)
            {
                await File.WriteAllTextAsync(seedPath, seedText, cancellationToken);
            }

            await File.WriteAllTextAsync(stampPath, version, cancellationToken);
        }
        catch
        {
            // Best-effort only. Bundled seed copy must never block XML loading.
        }
    }

    private static string? GetSeedVersion(BethesdaFranchise franchise)
        => franchise switch
        {
            BethesdaFranchise.Fallout => "v1",
            _ => null,
        };

    private static string GetSeedFileName(BethesdaFranchise franchise)
        => franchise switch
        {
            BethesdaFranchise.Fallout => "bundled-fallout4-franchise-tm.tsv",
            _ => "bundled-franchise-tm.tsv",
        };

    private static async Task RepairExistingSeedIfNeededAsync(string seedPath, string seedText, CancellationToken cancellationToken)
    {
        if (!File.Exists(seedPath))
        {
            return;
        }

        var existingText = await File.ReadAllTextAsync(seedPath, cancellationToken);
        if (string.Equals(existingText, seedText, StringComparison.Ordinal))
        {
            return;
        }

        await File.WriteAllTextAsync(seedPath, seedText, cancellationToken);
    }
}
