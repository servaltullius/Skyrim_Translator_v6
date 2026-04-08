using System;
using System.IO;

namespace XTranslatorAi.Tests;

public class FranchiseTranslationMemoryPathTests
{
    [Fact]
    public void ProjectPaths_ElderScrollsBranch_UsesLegacyGlobalGlossaryFilename()
    {
        var source = ReadProjectPathsSource();
        var elderScrollsBranch = ExtractBetween(
            source,
            "if (franchise == BethesdaFranchise.ElderScrolls)",
            "var franchiseDir = franchise switch"
        );
        var lines = elderScrollsBranch
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal("{", lines[0]);
        Assert.Equal("return Path.Combine(baseDir, \"global-glossary.sqlite\");", lines[^2]);
        Assert.Equal("}", lines[^1]);
        Assert.DoesNotContain("/starfield/", elderScrollsBranch, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/fallout/", elderScrollsBranch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectPaths_StarfieldBranch_UsesDedicatedStarfieldDirectory()
    {
        var source = ReadProjectPathsSource();
        var franchiseBranch = ExtractBetween(
            source,
            "var franchiseDir = franchise switch",
            "public static string GetGlobalTranslationMemoryImportDir"
        );

        AssertInOrder(
            franchiseBranch,
            "BethesdaFranchise.Starfield => \"starfield\",",
            "var dir = Path.Combine(baseDir, franchiseDir);",
            "return Path.Combine(dir, \"global-glossary.sqlite\");"
        );
    }

    private static string ReadProjectPathsSource()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine(repoRoot, "src", "XTranslatorAi.App", "Services", "ProjectPaths.cs");
        return File.ReadAllText(path);
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
