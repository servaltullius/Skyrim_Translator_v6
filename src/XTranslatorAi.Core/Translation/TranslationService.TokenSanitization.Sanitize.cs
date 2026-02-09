using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static string RemoveBrokenXtTokenMarkers(string text)
    {
        var idx = text.IndexOf("__XT_", StringComparison.Ordinal);
        if (idx < 0)
        {
            return text;
        }

        var sb = new StringBuilder(capacity: text.Length);
        var cursor = 0;

        while (idx >= 0)
        {
            sb.Append(text.AsSpan(cursor, idx - cursor));

            var m = XtTokenRegex.Match(text, idx);
            if (m.Success && m.Index == idx)
            {
                sb.Append(m.Value);
                cursor = idx + m.Length;
            }
            else
            {
                var end = idx;
                while (end < text.Length && IsXtTokenChar(text[end]))
                {
                    end++;
                }
                cursor = end;
            }

            idx = text.IndexOf("__XT_", cursor, StringComparison.Ordinal);
        }

        sb.Append(text.AsSpan(cursor));
        return sb.ToString();
    }

    private static bool IsXtTokenChar(char c)
        => c == '_' || (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static string SanitizeModelTranslationText(string text)
        => SanitizeModelTranslationText(text, inputText: null);

    private static string SanitizeModelTranslationText(string text, string? inputText)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var working = text;

        // Source tags and pagebreak markers are masked as __XT_PH_****__ tokens.
        // If the model hallucinates raw tags or [pagebreak], they will corrupt formatting. Strip them.
        // Exception: Some modes intentionally keep certain runtime tags (e.g., <mag>/<dur>) visible to the model.
        // In that case, preserve only the tags that were present in the INPUT, and strip everything else.
        if (working.IndexOf('<') >= 0)
        {
            if (!string.IsNullOrWhiteSpace(inputText) && inputText.IndexOf('<') >= 0)
            {
                // Normalize common Skyrim placeholders that models sometimes output with extra spaces/casing.
                if (inputText.IndexOf("<mag", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    working = Regex.Replace(
                        working,
                        pattern: @"(?<sign>[+-]?)<\s*mag\s*>",
                        evaluator: m => m.Groups["sign"].Value + "<mag>",
                        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                    );
                }
                if (inputText.IndexOf("<dur", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    working = Regex.Replace(
                        working,
                        pattern: @"(?<sign>[+-]?)<\s*dur\s*>",
                        evaluator: m => m.Groups["sign"].Value + "<dur>",
                        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                    );
                }
                if (inputText.IndexOf("<bur", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    working = Regex.Replace(
                        working,
                        pattern: @"(?<sign>[+-]?)<\s*bur\s*>",
                        evaluator: m => m.Groups["sign"].Value + "<bur>",
                        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
                    );
                }

                var allowed = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match m in RawMarkupTagRegex.Matches(inputText))
                {
                    allowed.Add(m.Value);
                }

                if (allowed.Count == 0)
                {
                    working = RawMarkupTagRegex.Replace(working, "");
                }
                else
                {
                    working = RawMarkupTagRegex.Replace(working, m => allowed.Contains(m.Value) ? m.Value : "");
                }
            }
            else
            {
                working = RawMarkupTagRegex.Replace(working, "");
            }
        }
        if (working.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var allowPagebreak = !string.IsNullOrWhiteSpace(inputText)
                                 && inputText.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!allowPagebreak)
            {
                working = RawPagebreakRegex.Replace(working, "");
            }
        }

        // Line breaks are also masked into tokens, so any raw CR/LF here is extra.
        if (working.IndexOf('\n') >= 0 || working.IndexOf('\r') >= 0)
        {
            working = working.Replace('\r', ' ').Replace('\n', ' ');
        }

        if (!string.IsNullOrWhiteSpace(inputText))
        {
            working = PromptLeakCleaner.StripLeakedPlaceholderInstructions(inputText, working);
        }

        return working;
    }
}
