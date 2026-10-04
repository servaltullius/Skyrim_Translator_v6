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

    // Plain __XT_PH_####__ tokens are layout: line breaks, page breaks and formatting tags.
    private static readonly Regex LayoutTokenRegex = new(
        pattern: @"__XT_PH_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    // A sentence end followed by space ("cold. The", "dead!\" She"); "。" needs no space.
    private static readonly Regex SentenceEndRegex = new(
        pattern: @"[.!?][""'”’)\]]*\s+|。[""'”’)\]]*\s*",
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

        if (piece.Length > maxChunkChars)
        {
            // The piece itself runs past the limit, so no boundary in the current chunk avoids a cut inside it.
            if (state.Sb.Length > 0)
            {
                FlushChunk(state);
            }

            state.Chunks.AddRange(SplitPlainText(piece, maxChunkChars));
            return;
        }

        var tokenCount = IsTokenPiece(piece) ? 1 : 0;

        // Cutting right before the piece that no longer fits split sentences in the middle, usually before a
        // term token ("You must defeat the | __XT_TERM_0003__ at the summit"), and each half was translated
        // on its own. Cut the chunk at its last line break or sentence end and carry the rest over instead.
        while (state.Sb.Length > 0
               && (ShouldFlushForTokenLimit(state.Sb, state.CurrentTokens, tokenCount, maxTokensPerChunk)
                   || state.Sb.Length + piece.Length > maxChunkChars))
        {
            FlushAtBoundary(state);
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

    /// <summary>Flushes the chunk up to <see cref="FindChunkCut"/>, keeping the rest; the whole chunk when there is no boundary.</summary>
    private static void FlushAtBoundary(SplitState state)
    {
        var text = state.Sb.ToString();
        var cut = FindChunkCut(text);
        if (cut <= 0 || cut >= text.Length)
        {
            FlushChunk(state);
            return;
        }

        state.Chunks.Add(text[..cut]);
        var rest = text[cut..];
        state.Sb.Clear().Append(rest);
        state.CurrentTokens = TokenRegex.Matches(rest).Count;
    }

    /// <summary>
    /// After the last layout token when that keeps at least half of the chunk; otherwise after the later of the
    /// last layout token and the last sentence end (a title's line break near the start alone would send a tiny
    /// request and leave the long rest to be cut again). -1 when there is neither.
    /// </summary>
    private static int FindChunkCut(string text)
    {
        var layoutCut = -1;
        foreach (Match m in LayoutTokenRegex.Matches(text))
        {
            layoutCut = m.Index + m.Length;
        }

        if (layoutCut >= text.Length / 2)
        {
            return layoutCut;
        }

        return Math.Max(layoutCut, LastSentenceCut(text));
    }

    private static int LastSentenceCut(string text)
    {
        var cut = -1;
        foreach (Match m in SentenceEndRegex.Matches(text))
        {
            cut = m.Index + m.Length;
        }

        return cut;
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
        // A paragraph longer than the limit: end at a sentence in the second half before settling for a space.
        var sentenceCut = LastSentenceCut(text.Substring(start, maxLen));
        if (sentenceCut >= maxLen / 2)
        {
            return sentenceCut;
        }

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
