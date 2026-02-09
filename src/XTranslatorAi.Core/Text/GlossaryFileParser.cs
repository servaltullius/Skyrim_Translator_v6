using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static class GlossaryFileParser
{
    private static readonly Regex PairRegex = new(
        pattern: "\"(?<src>[^\"\\r\\n]+)\"\\s*:\\s*\"(?<dst>[^\"\\r\\n]+)\"",
        options: RegexOptions.CultureInvariant
    );

    public static IReadOnlyList<(string Source, string Target)> ParsePairs(string fileText)
    {
        if (fileText == null)
        {
            throw new ArgumentNullException(nameof(fileText));
        }

        var list = new List<(string Source, string Target)>();
        foreach (Match match in PairRegex.Matches(fileText))
        {
            var src = match.Groups["src"].Value.Trim();
            var dst = match.Groups["dst"].Value.Trim();
            if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
            {
                continue;
            }
            list.Add((src, dst));
        }
        return list;
    }

    public static IReadOnlyList<(string? Category, string Source, string Target)> ParseEntries(string fileText)
    {
        if (fileText == null)
        {
            throw new ArgumentNullException(nameof(fileText));
        }

        var entries = TryParseCategorizedEntries(fileText);
        if (entries.Count > 0)
        {
            return entries;
        }

        return ParsePairs(fileText).Select(p => ((string?)null, p.Source, p.Target)).ToList();
    }

    private static List<(string? Category, string Source, string Target)> TryParseCategorizedEntries(string text)
    {
        var entries = new List<(string? Category, string Source, string Target)>();
        var i = 0;
        var depth = 0;
        string? currentCategory = null;
        int? currentCategoryObjectDepth = null;

        // Skip UTF-8 BOM if present.
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            i++;
        }

        while (i < text.Length)
        {
            SkipTrivia(text, ref i);
            if (i >= text.Length)
            {
                break;
            }

            var ch = text[i];
            if (ch == '{')
            {
                depth++;
                i++;
                continue;
            }
            if (ch == '}')
            {
                depth = Math.Max(0, depth - 1);
                if (currentCategoryObjectDepth != null && depth < currentCategoryObjectDepth.Value)
                {
                    currentCategory = null;
                    currentCategoryObjectDepth = null;
                }
                i++;
                continue;
            }

            if (ch != '"')
            {
                i++;
                continue;
            }

            var key = ReadQuotedString(text, ref i);
            SkipTrivia(text, ref i);
            if (i >= text.Length || text[i] != ':')
            {
                continue;
            }

            i++; // :
            var afterColon = i;

            // Category header line format:
            //   "Category":
            // (i.e., colon is at end-of-line, followed by the next line's pairs)
            if (IsEndOfLineAfterColon(text, afterColon))
            {
                currentCategory = key.Trim();
                currentCategoryObjectDepth = null;
                continue;
            }

            SkipTrivia(text, ref i);
            if (i >= text.Length)
            {
                break;
            }

            // Category start: "Category": { ... }
            if (text[i] == '{')
            {
                currentCategory = key.Trim();
                depth++;
                currentCategoryObjectDepth = depth;
                i++; // consume '{'
                continue;
            }

            // Term pair: "src": "dst"
            if (text[i] == '"')
            {
                var value = ReadQuotedString(text, ref i);
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                {
                    entries.Add((currentCategory, key.Trim(), value.Trim()));
                }
            }
        }

        return entries;
    }

    private static bool IsEndOfLineAfterColon(string text, int i)
    {
        // Skip spaces/tabs only (do not cross newline). If we hit newline or a comment, it's a header line.
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == ' ' || ch == '\t')
            {
                i++;
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                return true;
            }

            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                return true;
            }

            return false;
        }

        return true;
    }

    private static void SkipTrivia(string text, ref int i)
    {
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            // Line comment: //...
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                i += 2;
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
                continue;
            }

            break;
        }
    }

    private static string ReadQuotedString(string text, ref int i)
    {
        if (i >= text.Length || text[i] != '"')
        {
            return "";
        }

        i++; // opening quote
        var sb = new System.Text.StringBuilder(capacity: 64);

        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '"')
            {
                i++; // closing quote
                return sb.ToString();
            }

            if (ch == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                i += 2;
                sb.Append(
                    next switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => next,
                    }
                );
                continue;
            }

            sb.Append(ch);
            i++;
        }

        return "";
    }
}
