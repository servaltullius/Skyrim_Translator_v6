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
