using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static void SplitByTokens(string text, out List<string> texts, out List<string> tokens)
    {
        texts = new List<string>();
        tokens = new List<string>();

        var idx = 0;
        foreach (Match m in XtTokenRegex.Matches(text))
        {
            if (m.Index > idx)
            {
                texts.Add(text.Substring(idx, m.Index - idx));
            }
            else
            {
                texts.Add("");
            }

            tokens.Add(m.Value);
            idx = m.Index + m.Length;
        }

        if (idx < text.Length)
        {
            texts.Add(text.Substring(idx));
        }
        else
        {
            texts.Add("");
        }
    }

    private static string JoinTextAndTokens(IReadOnlyList<string> texts, IReadOnlyList<string> tokens)
    {
        var capacity = 0;
        foreach (var t in texts)
        {
            capacity += t.Length;
        }
        foreach (var t in tokens)
        {
            capacity += t.Length;
        }
        var sb = new StringBuilder(capacity: capacity);
        var count = tokens.Count;
        for (var i = 0; i < count; i++)
        {
            sb.Append(texts[i]);
            sb.Append(tokens[i]);
        }
        sb.Append(texts[count]);
        return sb.ToString();
    }

    private static List<string> ExtractTokens(string text)
    {
        var matches = XtTokenRegex.Matches(text);
        var tokens = new List<string>(capacity: matches.Count);
        foreach (Match m in matches)
        {
            tokens.Add(m.Value);
        }
        return tokens;
    }

    private static Dictionary<string, int> CountTokens(IEnumerable<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (counts.TryGetValue(token, out var n))
            {
                counts[token] = n + 1;
            }
            else
            {
                counts[token] = 1;
            }
        }
        return counts;
    }
}
