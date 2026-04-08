using System;
using System.IO;
using System.Reflection;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmSeedServiceTests
{
    [Fact]
    public void LoadBundledFranchiseTmSeed_Fallout_ReturnsEmbeddedSeedText()
    {
        var embeddedAssetsType = LoadEmbeddedAssetsType();
        var method = embeddedAssetsType.GetMethod(
            "LoadBundledFranchiseTmSeed",
            BindingFlags.Public | BindingFlags.Static
        );

        Assert.NotNull(method);

        var result = method!.Invoke(null, new object[] { BethesdaFranchise.Fallout });
        var seed = Assert.IsType<string>(result);

        Assert.Contains("Source\tTarget", seed, StringComparison.Ordinal);
        Assert.Contains("Pip-Boy", seed, StringComparison.Ordinal);
    }

    private static Type LoadEmbeddedAssetsType()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var appAssemblyPath = FindAppAssemblyPath(repoRoot);
        var appAssembly = Assembly.LoadFrom(appAssemblyPath);

        return appAssembly.GetType("XTranslatorAi.App.Services.EmbeddedAssets", throwOnError: true)!;
    }

    private static string FindAppAssemblyPath(string repoRoot)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", "XTranslatorAi.App", "bin", "Release", "net8.0-windows", "TulliusTranslator.dll"),
            Path.Combine(repoRoot, "src", "XTranslatorAi.App", "bin", "Debug", "net8.0-windows", "TulliusTranslator.dll"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate the built XTranslatorAi.App assembly.", candidates[0]);
    }
}
