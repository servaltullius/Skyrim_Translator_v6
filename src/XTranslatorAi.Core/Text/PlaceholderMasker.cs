using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public sealed record PlaceholderMaskerOptions(bool KeepSkyrimTagsRaw = false);

public sealed class PlaceholderMasker
{
    private static readonly Regex PlaceholderRegex = new(
        pattern: @"(\r\n|\r|\n|[+-]?<[^>]+>[\t ]*%|[+-]?<[^>]+>|\[pagebreak\]|%[A-Za-z0-9_]+%|%(?:[0-9]+\$)?[-+0-9.]*[A-Za-z]|\$[A-Za-z0-9_]+\$|\{\{[A-Za-z0-9_.,:+-]{1,40}\}\}|\{[A-Za-z0-9_.,:+-]{1,40}\}|[+-]?\d+(?:\.\d+)?[\t ]*%|%)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private readonly PlaceholderMaskerOptions _options;

    public PlaceholderMasker()
        : this(new PlaceholderMaskerOptions())
    {
    }

    public PlaceholderMasker(PlaceholderMaskerOptions options)
    {
        _options = options ?? new PlaceholderMaskerOptions();
    }

    public MaskedText Mask(string text)
    {
        var tokenToOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
        var masked = PlaceholderRegex.Replace(
            text,
            m =>
            {
                var original = m.Value;
                if (_options.KeepSkyrimTagsRaw && ShouldKeepRawSkyrimTag(original))
                {
                    return original;
                }

                var idx = tokenToOriginal.Count;

                // Reserve __XT_PH_9999__ for the end-of-text sentinel used during translation.
                if (idx >= 9999)
                {
                    throw new InvalidOperationException("Too many placeholders in a single string (>= 9999).");
                }

                var label = TryGetSemanticPlaceholderLabel(original, text, m.Index, m.Length);
                var token = label == null ? $"__XT_PH_{idx:0000}__" : $"__XT_PH_{label}_{idx:0000}__";
                tokenToOriginal[token] = original;
                return token;
            }
        );

        return new MaskedText(masked, tokenToOriginal);
    }

    private static bool ShouldKeepRawSkyrimTag(string placeholder)
    {
        if (string.IsNullOrWhiteSpace(placeholder))
        {
            return false;
        }

        var s = StripLeadingSign(placeholder.AsSpan()).Trim();
        _ = TryStripPercentSuffix(ref s);

        if (!TryGetAngleInner(s, out var inner))
        {
            return false;
        }

        // Common Skyrim/Bethesda runtime placeholders used in magic-effect descriptions.
        return inner.Equals("mag", StringComparison.OrdinalIgnoreCase)
               || inner.Equals("dur", StringComparison.OrdinalIgnoreCase)
               || inner.Equals("bur", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetSemanticPlaceholderLabel(string placeholder, string fullText, int matchIndex, int matchLength)
    {
        if (string.IsNullOrEmpty(placeholder))
        {
            return null;
        }

        var s = StripLeadingSign(placeholder.AsSpan());
        var hasPercentSuffix = TryStripPercentSuffix(ref s);

        if (hasPercentSuffix && IsAsciiNumber(s))
        {
            return "NUM";
        }

        if (!TryGetAngleInner(s, out var inner))
        {
            return null;
        }

        if (inner.Equals("mag", StringComparison.OrdinalIgnoreCase))
        {
            return "MAG";
        }
        if (inner.Equals("dur", StringComparison.OrdinalIgnoreCase))
        {
            return "DUR";
        }

        // Percent placeholders sometimes appear inside the angle brackets (e.g., "<100%>").
        // Treat those as numeric placeholders.
        if (TryGetNumericPercentInner(inner, out _))
        {
            return "NUM";
        }

        if (IsAsciiDigits(inner))
        {
            return GetNumericAngleLabel(hasPercentSuffix, fullText, matchIndex, matchLength);
        }

        return null;
    }

    private static ReadOnlySpan<char> StripLeadingSign(ReadOnlySpan<char> s)
    {
        if (!s.IsEmpty && s[0] is '+' or '-')
        {
            return s.Slice(1);
        }
        return s;
    }

    private static bool TryStripPercentSuffix(ref ReadOnlySpan<char> s)
    {
        if (!s.IsEmpty && s[^1] == '%')
        {
            s = s.Slice(0, s.Length - 1).TrimEnd();
            return true;
        }
        return false;
    }

    private static bool TryGetAngleInner(ReadOnlySpan<char> s, out ReadOnlySpan<char> inner)
    {
        inner = default;
        if (s.Length < 3 || s[0] != '<' || s[^1] != '>')
        {
            return false;
        }

        inner = s.Slice(1, s.Length - 2).Trim();
        return true;
    }

    private static bool TryGetNumericPercentInner(ReadOnlySpan<char> inner, out ReadOnlySpan<char> numeric)
    {
        numeric = default;
        if (inner.Length < 2 || inner[^1] != '%')
        {
            return false;
        }

        var n = inner.Slice(0, inner.Length - 1).TrimEnd();
        if (!IsAsciiNumber(n))
        {
            return false;
        }

        numeric = n;
        return true;
    }

    private static string GetNumericAngleLabel(bool hasPercentSuffix, string fullText, int matchIndex, int matchLength)
    {
        if (hasPercentSuffix)
        {
            return "NUM";
        }

        // Numeric placeholders like <15> are usually magnitudes, but some strings use <30> seconds style
        // placeholders for duration. Detect common duration contexts so the prompt can treat it as time.
        if (IsLikelyDurationPlaceholder(fullText, matchIndex, matchLength))
        {
            return "DUR";
        }

        return "NUM";
    }

    private static bool IsAsciiNumber(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty)
        {
            return false;
        }

        var sawDigit = false;
        foreach (var c in s)
        {
            if (c >= '0' && c <= '9')
            {
                sawDigit = true;
                continue;
            }

            if (c == '.')
            {
                continue;
            }

            return false;
        }

        return sawDigit;
    }

    private static bool IsLikelyDurationPlaceholder(string fullText, int matchIndex, int matchLength)
    {
        if (string.IsNullOrEmpty(fullText) || matchIndex < 0 || matchLength <= 0)
        {
            return false;
        }

        var afterIdx = matchIndex + matchLength;
        if (afterIdx < 0 || afterIdx >= fullText.Length)
        {
            return false;
        }

        // Look for "seconds" right after the placeholder: "<120> seconds"
        var remaining = fullText.Length - afterIdx;
        var take = Math.Min(24, remaining);
        if (take <= 0)
        {
            return false;
        }

        var after = fullText.Substring(afterIdx, take);
        return Regex.IsMatch(after, @"^\s*seconds?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    public string Unmask(string text, IReadOnlyDictionary<string, string> tokenToOriginal)
    {
        foreach (var token in tokenToOriginal.Keys)
        {
            if (!text.Contains(token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Missing placeholder token in translation: {token}");
            }
        }

        var working = text;
        foreach (var (token, original) in tokenToOriginal)
        {
            working = working.Replace(token, original, StringComparison.Ordinal);
        }
        return working;
    }
}

public sealed record MaskedText(string Text, IReadOnlyDictionary<string, string> TokenToOriginal);
