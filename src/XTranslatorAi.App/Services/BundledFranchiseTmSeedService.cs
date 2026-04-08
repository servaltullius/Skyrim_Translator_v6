using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public sealed class BundledFranchiseTmSeedService
{
    public const string FalloutSeedVersion = "fallout4-v1";

    private readonly string? _globalRootOverride;

    public BundledFranchiseTmSeedService(string? globalRootOverride = null)
    {
        _globalRootOverride = globalRootOverride;
    }

    public async Task EnsureSeedAsync(BethesdaFranchise franchise, CancellationToken cancellationToken)
    {
        if (franchise != BethesdaFranchise.Fallout)
        {
            return;
        }

        try
        {
            var seed = EmbeddedAssets.LoadBundledFranchiseTmSeed(franchise);
            if (string.IsNullOrWhiteSpace(seed))
            {
                return;
            }

            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(franchise, _globalRootOverride);
            var stampPath = Path.Combine(importDir, $".bundled-franchise-tm-seed.{FalloutSeedVersion}.stamp");
            if (File.Exists(stampPath))
            {
                return;
            }

            var seedPath = Path.Combine(importDir, $"bundled-fallout-franchise-tm-seed.{FalloutSeedVersion}.tsv");
            if (!File.Exists(seedPath))
            {
                await File.WriteAllTextAsync(seedPath, seed, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            }

            await File.WriteAllTextAsync(
                stampPath,
                $"seed={Path.GetFileName(seedPath)}{Environment.NewLine}version={FalloutSeedVersion}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
        }
        catch
        {
            // Seed injection is best-effort and must not block project load or XML import.
        }
    }
}
