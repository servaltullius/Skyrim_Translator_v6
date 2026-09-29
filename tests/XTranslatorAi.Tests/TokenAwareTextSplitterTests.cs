using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class TokenAwareTextSplitterTests
{
    private static readonly Regex TokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    [Fact]
    public void Split_DoesNotSplitInsideTokens_AndRoundTrips()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            sb.Append("AAA ");
            sb.Append($"__XT_PH_{i:0000}__");
            sb.Append(" BBB ");
            sb.Append($"__XT_TERM_{i:0000}__");
            sb.Append(" CCC ");
        }
        var text = sb.ToString();

        var chunks = TokenAwareTextSplitter.Split(text, maxChunkChars: 80);
        Assert.True(chunks.Count > 1);
        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => Assert.True(c.Length <= 80));

        var boundaries = chunks
            .Take(chunks.Count - 1)
            .Select((c, idx) => chunks.Take(idx + 1).Sum(x => x.Length))
            .ToList();

        var matches = TokenRegex.Matches(text).Cast<Match>().ToList();
        foreach (var boundary in boundaries)
        {
            foreach (var m in matches)
            {
                var start = m.Index;
                var end = m.Index + m.Length;
                Assert.False(start < boundary && boundary < end, $"Boundary {boundary} falls inside token {m.Value}");
            }
        }
    }

    [Fact]
    public void Split_LongPlainText_RespectsMaxLength_AndRoundTrips()
    {
        var text = new string('A', 1000);
        var chunks = TokenAwareTextSplitter.Split(text, maxChunkChars: 123);

        Assert.Equal(text, string.Concat(chunks));
        Assert.All(chunks, c => Assert.True(c.Length <= 123));
        Assert.DoesNotContain(chunks, c => c.Length == 0);
    }

    // --- SplitAtPagebreaks tests ---

    [Fact]
    public void SplitAtPagebreaks_NoPagebreaks_ReturnsSingleChunk()
    {
        var text = "Hello world, no pagebreaks here.";
        var tokenMap = new Dictionary<string, string>();

        var chunks = TokenAwareTextSplitter.SplitAtPagebreaks(text, maxCharsPerChunk: 500, tokenMap);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void SplitAtPagebreaks_ThreePages_ReturnsThreeChunks()
    {
        // Simulate masked text with [pagebreak] tokens
        var text = "Page one content.__XT_PH_0001__Page two content.__XT_PH_0002__Page three content.";
        var tokenMap = new Dictionary<string, string>
        {
            ["__XT_PH_0001__"] = "[pagebreak]",
            ["__XT_PH_0002__"] = "[pagebreak]",
        };

        var chunks = TokenAwareTextSplitter.SplitAtPagebreaks(text, maxCharsPerChunk: 40, tokenMap);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Page one content.__XT_PH_0001__", chunks[0]);
        Assert.Equal("Page two content.__XT_PH_0002__", chunks[1]);
        Assert.Equal("Page three content.", chunks[2]);
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void SplitAtPagebreaks_LongPage_SubSplits()
    {
        var longPage = new string('A', 300);
        var text = $"{longPage}__XT_PH_0001__Short page.";
        var tokenMap = new Dictionary<string, string>
        {
            ["__XT_PH_0001__"] = "[pagebreak]",
        };

        var chunks = TokenAwareTextSplitter.SplitAtPagebreaks(text, maxCharsPerChunk: 100, tokenMap);

        Assert.True(chunks.Count >= 3, $"Expected at least 3 chunks for long page, got {chunks.Count}");
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void SplitAtPagebreaks_ShortPages_MergesWithinLimit()
    {
        var text = "A.__XT_PH_0001__B.__XT_PH_0002__C.__XT_PH_0003__D.";
        var tokenMap = new Dictionary<string, string>
        {
            ["__XT_PH_0001__"] = "[pagebreak]",
            ["__XT_PH_0002__"] = "[pagebreak]",
            ["__XT_PH_0003__"] = "[pagebreak]",
        };

        // maxCharsPerChunk=500 is large enough to merge all segments into one
        var chunks = TokenAwareTextSplitter.SplitAtPagebreaks(text, maxCharsPerChunk: 500, tokenMap);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void SplitAtPagebreaks_NonPagebreakTokens_Ignored()
    {
        var text = "Value is __XT_PH_0001__ gold.__XT_PH_0002__Next page.";
        var tokenMap = new Dictionary<string, string>
        {
            ["__XT_PH_0001__"] = "<mag>",        // not a pagebreak
            ["__XT_PH_0002__"] = "[pagebreak]",   // pagebreak
        };

        var chunks = TokenAwareTextSplitter.SplitAtPagebreaks(text, maxCharsPerChunk: 50, tokenMap);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Value is __XT_PH_0001__ gold.__XT_PH_0002__", chunks[0]);
        Assert.Equal("Next page.", chunks[1]);
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void SplitAtPagebreaks_RoundTrips()
    {
        var text = "First page content here.__XT_PH_0001__Second page content here.__XT_PH_0002__Third.";
        var tokenMap = new Dictionary<string, string>
        {
            ["__XT_PH_0001__"] = "[pagebreak]",
            ["__XT_PH_0002__"] = "[pagebreak]",
        };

        var chunks = TokenAwareTextSplitter.SplitAtPagebreaks(text, maxCharsPerChunk: 80, tokenMap);
        Assert.Equal(text, string.Concat(chunks));
    }
}
