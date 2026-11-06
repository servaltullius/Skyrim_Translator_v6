using System;
using System.IO;

namespace XTranslatorAi.Tests;

public class BundledFranchiseTmWorkflowDocsTests
{
    [Fact]
    public void Readme_BundledFranchiseTmSection_DescribesTheBuiltInSeedContract()
    {
        var readme = ReadRepoFile("README.md");

        Assert.Contains("Fallout TM은 지금 Fallout 4 계열 데이터만 다룹니다.", readme, StringComparison.Ordinal);
        Assert.Contains("내장 Fallout TM은 Fallout 프로젝트를 처음 열 때 자동으로 넣습니다.", readme, StringComparison.Ordinal);
        Assert.Contains("내장 Skyrim/TES TM은 Elder Scrolls 프로젝트를 처음 열 때 자동으로 넣습니다.", readme, StringComparison.Ordinal);
        Assert.Contains("내장 Starfield TM은 Starfield 프로젝트를 처음 열 때 자동으로 넣습니다.", readme, StringComparison.Ordinal);
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
