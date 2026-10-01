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
        // The Fallout seed is only three English-preserving pairs. Do not describe it as a Korean TM.
        Assert.Contains("영문 유지 3쌍", readme, StringComparison.Ordinal);
        Assert.Contains("직접 만든 TSV는 고급 설정의 `시리즈 TM 가져오기`로 계속 가져올 수 있습니다.", readme, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, relativePath);
        return File.ReadAllText(path);
    }
}
