using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static class TokenAwareTextSplitter
{
    private static readonly Regex TokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private sealed class SplitState
    {
        public SplitState(int maxChunkChars)
        {
            Chunks = new List<string>();
            Sb = new StringBuilder(capacity: Math.Min(maxChunkChars, 4096));
        }

        public List<string> Chunks { get; }
        public StringBuilder Sb { get; }
        public int CurrentTokens { get; set; }
    }

    /// <summary>
    /// Splits masked text preferring page break token boundaries ([pagebreak], [page break], &lt;page break&gt;).
    /// Falls back to <see cref="Split"/> for segments that exceed <paramref name="maxCharsPerChunk"/>.
    /// Short adjacent segments are merged up to the char limit for efficiency.
    /// </summary>
    public static IReadOnlyList<string> SplitAtPagebreaks(
        string text,
        int maxCharsPerChunk,
        IReadOnlyDictionary<string, string> tokenToOriginal)
    {
        if (maxCharsPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharsPerChunk));
        }

        if (string.IsNullOrEmpty(text))
        {
            return new[] { text ?? "" };
        }

        var pagebreakPositions = FindPagebreakTokenPositions(text, tokenToOriginal);
        if (pagebreakPositions.Count == 0)
        {
            return text.Length <= maxCharsPerChunk
                ? new[] { text }
                : Split(text, maxCharsPerChunk);
        }

        // Split into segments at pagebreak boundaries.
        // Each segment includes its trailing pagebreak token (except possibly the last).
        var segments = BuildPagebreakSegments(text, pagebreakPositions);

        // Merge short adjacent segments up to maxCharsPerChunk.
        return MergePagebreakSegments(segments, maxCharsPerChunk);
    }

    private static List<(int Position, int Length)> FindPagebreakTokenPositions(
        string text,
        IReadOnlyDictionary<string, string> tokenToOriginal)
    {
        var positions = new List<(int Position, int Length)>();
        foreach (Match m in TokenRegex.Matches(text))
        {
            // Every page break form, not only Skyrim's "[pagebreak]": LotD's "<page break>" books were
            // split by length instead (see ProtectedTextKinds.PageBreakPattern).
            if (tokenToOriginal.TryGetValue(m.Value, out var original) && ProtectedTextKinds.IsPageBreak(original))
            {
                positions.Add((m.Index, m.Length));
            }
        }

        return positions;
    }

    private static List<string> BuildPagebreakSegments(
        string text,
        List<(int Position, int Length)> pagebreakPositions)
    {
        var segments = new List<string>();
        var startIdx = 0;

        foreach (var (pos, length) in pagebreakPositions)
        {
            var endIdx = pos + length;
            if (endIdx > startIdx)
            {
                segments.Add(text[startIdx..endIdx]);
            }

            startIdx = endIdx;
        }

        if (startIdx < text.Length)
        {
            segments.Add(text[startIdx..]);
        }

        return segments;
    }

    private static IReadOnlyList<string> MergePagebreakSegments(List<string> segments, int maxCharsPerChunk)
    {
        if (segments.Count <= 1)
        {
            // Single segment that might still be too long.
            if (segments.Count == 1 && segments[0].Length > maxCharsPerChunk)
            {
                return Split(segments[0], maxCharsPerChunk);
            }

            return segments;
        }

        var chunks = new List<string>();
        var sb = new StringBuilder(capacity: Math.Min(maxCharsPerChunk, 4096));

        foreach (var seg in segments)
        {
            // If appending this segment would exceed the limit, flush current.
            if (sb.Length > 0 && sb.Length + seg.Length > maxCharsPerChunk)
            {
                chunks.Add(sb.ToString());
                sb.Clear();
            }

            // If segment alone exceeds the limit, sub-split it.
            if (seg.Length > maxCharsPerChunk && sb.Length == 0)
            {
                var subParts = Split(seg, maxCharsPerChunk);
                chunks.AddRange(subParts);
                continue;
            }

            sb.Append(seg);
        }

        if (sb.Length > 0)
        {
            chunks.Add(sb.ToString());
        }

        return chunks.Count == 0 ? new[] { string.Concat(segments) } : chunks;
    }

    public static IReadOnlyList<string> Split(string text, int maxChunkChars, int? maxTokensPerChunk = null)
    {
        if (maxChunkChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkChars), maxChunkChars, "maxChunkChars must be > 0.");
        }
        if (maxTokensPerChunk is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokensPerChunk), maxTokensPerChunk, "maxTokensPerChunk must be null or > 0.");
        }

        if (text.Length <= maxChunkChars && maxTokensPerChunk == null)
        {
            return new[] { text };
        }

        return SplitCore(text, maxChunkChars, maxTokensPerChunk);
    }

    private static IReadOnlyList<string> SplitCore(string text, int maxChunkChars, int? maxTokensPerChunk)
    {
        var pieces = SplitIntoPieces(text);
        var state = new SplitState(maxChunkChars);

        foreach (var piece in pieces)
        {
            AddPiece(state, piece, maxChunkChars, maxTokensPerChunk);
        }

        if (state.Sb.Length > 0)
        {
            state.Chunks.Add(state.Sb.ToString());
        }

        if (state.Chunks.Count == 0)
        {
            return new[] { text };
        }

        return state.Chunks;
    }

    private static void AddPiece(SplitState state, string piece, int maxChunkChars, int? maxTokensPerChunk)
    {
        if (piece.Length == 0)
        {
            return;
        }

        if (state.Sb.Length == 0 && piece.Length > maxChunkChars)
        {
            state.Chunks.AddRange(SplitPlainText(piece, maxChunkChars));
            return;
        }

        var tokenCount = IsTokenPiece(piece) ? 1 : 0;

        if (ShouldFlushForTokenLimit(state.Sb, state.CurrentTokens, tokenCount, maxTokensPerChunk))
        {
            FlushChunk(state);
        }

        if (state.Sb.Length > 0 && state.Sb.Length + piece.Length > maxChunkChars)
        {
            FlushChunk(state);
        }

        if (piece.Length > maxChunkChars)
        {
            state.Chunks.AddRange(SplitPlainText(piece, maxChunkChars));
            return;
        }

        state.Sb.Append(piece);
        state.CurrentTokens += tokenCount;
    }

    private static bool ShouldFlushForTokenLimit(StringBuilder sb, int currentTokens, int tokenCount, int? maxTokensPerChunk)
        => maxTokensPerChunk is > 0 && sb.Length > 0 && currentTokens + tokenCount > maxTokensPerChunk.Value;

    private static void FlushChunk(SplitState state)
    {
        state.Chunks.Add(state.Sb.ToString());
        state.Sb.Clear();
        state.CurrentTokens = 0;
    }

    private static bool IsTokenPiece(string piece)
    {
        if (piece.Length is < 8 or > 32)
        {
            return false;
        }
        if (!piece.StartsWith("__XT_", StringComparison.Ordinal))
        {
            return false;
        }

        return TokenRegex.IsMatch(piece);
    }

    private static IReadOnlyList<string> SplitIntoPieces(string text)
    {
        var pieces = new List<string>();
        var idx = 0;

        foreach (Match m in TokenRegex.Matches(text))
        {
            if (m.Index > idx)
            {
                pieces.Add(text.Substring(idx, m.Index - idx));
            }

            pieces.Add(m.Value);
            idx = m.Index + m.Length;
        }

        if (idx < text.Length)
        {
            pieces.Add(text.Substring(idx));
        }

        return pieces;
    }

    private static IEnumerable<string> SplitPlainText(string text, int maxChunkChars)
    {
        var idx = 0;
        while (idx < text.Length)
        {
            var remaining = text.Length - idx;
            var take = Math.Min(maxChunkChars, remaining);
            if (take == remaining)
            {
                yield return text.Substring(idx, take);
                yield break;
            }

            var splitAt = FindSplitLength(text, idx, take);
            yield return text.Substring(idx, splitAt);
            idx += splitAt;
        }
    }

    private static int FindSplitLength(string text, int start, int maxLen)
    {
        var end = start + maxLen;

        for (var i = end - 1; i > start; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i - start + 1;
            }
        }

        return maxLen;
    }
}
