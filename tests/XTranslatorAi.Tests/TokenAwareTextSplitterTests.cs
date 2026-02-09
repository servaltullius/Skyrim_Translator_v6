using System;
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
}
