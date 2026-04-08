using System;
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
}
