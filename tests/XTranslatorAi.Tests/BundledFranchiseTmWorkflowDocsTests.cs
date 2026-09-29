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
        // Only Fallout has an embedded seed. Do not document proposed TES/Starfield data as shipped.
        Assert.Contains("영문 유지 3쌍", readme, StringComparison.Ordinal);
        Assert.Contains("TES와 Starfield의 완성된 번역 TM은 내장되어 있지 않습니다.", readme, StringComparison.Ordinal);
        Assert.Contains("Operator-provided TSV imports still work through the existing Franchise TM import flow.", readme, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, relativePath);
        return File.ReadAllText(path);
    }
}
