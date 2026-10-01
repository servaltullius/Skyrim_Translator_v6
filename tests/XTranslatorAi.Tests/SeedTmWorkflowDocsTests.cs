using System;
using System.IO;

namespace XTranslatorAi.Tests;

public class SeedTmWorkflowDocsTests
{
    [Fact]
    public void StarfieldFranchiseTmDocs_DescribeTheImportContract()
    {
        var docs = ReadRepoFile(Path.Combine("docs", "starfield-franchise-tm.md"));

        var importSection = ExtractSection(
            docs,
            "## 앱 import",
            "## "
        );

        AssertInOrder(
            string.Join('\n', importSection),
            "## 앱 import",
            "- 가져오기 TSV는 `Source<TAB>Target` 형식이어야 한다.",
            "- 앱의 `게임 시리즈` 선택기를 `Starfield`로 설정한 뒤 고급 설정의 `시리즈 TM 가져오기`에서 `artifacts/tm/starfield-franchise-tm.tsv`를 선택한다."
        );
    }

    [Fact]
    public void ProjectPaths_StarfieldImportDir_RemainsFranchiseScoped()
    {
        var source = ReadRepoFile(Path.Combine("src", "XTranslatorAi.App", "Services", "ProjectPaths.cs"));
        var methodBody = ExtractBetween(
            source,
            "public static string GetGlobalTranslationMemoryImportDir(BethesdaFranchise franchise)",
            "private static string GetProjectsBaseDir()"
        );

        Assert.Contains("GetGlobalGlossaryDbPath(franchise)", methodBody, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(baseDir, \"tm-import\")", methodBody, StringComparison.Ordinal);
        Assert.Contains("Directory.CreateDirectory(dir);", methodBody, StringComparison.Ordinal);
        Assert.Contains("return dir;", methodBody, StringComparison.Ordinal);

        Assert.Contains("BethesdaFranchise.Starfield => \"starfield\"", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, relativePath);
        return File.ReadAllText(path);
    }

    private static string[] ExtractSection(string text, string startHeading, string nextHeadingPrefix)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        var startIndex = Array.FindIndex(lines, line => string.Equals(line, startHeading, StringComparison.Ordinal));
        Assert.True(startIndex >= 0, $"Could not find start heading '{startHeading}'.");

        var endIndex = lines.Length;
        for (var i = startIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(nextHeadingPrefix, StringComparison.Ordinal))
            {
                endIndex = i;
                break;
            }
        }

        while (endIndex > startIndex && string.IsNullOrWhiteSpace(lines[endIndex - 1]))
        {
            endIndex--;
        }

        return lines[startIndex..endIndex];
    }

    private static string ExtractBetween(string source, string startMarker, string endMarker)
    {
        var startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find start marker '{startMarker}'.");

        startIndex += startMarker.Length;
        var endIndex = source.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Could not find end marker '{endMarker}'.");

        return source[startIndex..endIndex];
    }

    private static void AssertInOrder(string source, params string[] snippets)
    {
        var currentIndex = 0;

        foreach (var snippet in snippets)
        {
            var nextIndex = source.IndexOf(snippet, currentIndex, StringComparison.Ordinal);
            Assert.True(nextIndex >= 0, $"Could not find '{snippet}' after index {currentIndex}.");
            currentIndex = nextIndex + snippet.Length;
        }
    }
}
