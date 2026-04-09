using System;
using System.IO;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmWorkflowDocsTests
{
    [Fact]
    public void Readme_BundledFranchiseTmSection_DescribesTheBuiltInSeedContract()
    {
        var readme = ReadRepoFile("README.md");

        Assert.Contains("Fallout TM is currently scoped to Fallout 4 family data.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Fallout TM is auto-seeded on first Fallout project load.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Skyrim/TES TM is auto-seeded on first Elder Scrolls project load.", readme, StringComparison.Ordinal);
        Assert.Contains("Bundled Starfield TM is auto-seeded on first Starfield project load.", readme, StringComparison.Ordinal);
        Assert.Contains("Operator-provided TSV imports still work through the existing Franchise TM import flow.", readme, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, relativePath);
        return File.ReadAllText(path);
    }
}
