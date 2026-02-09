using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<\s*[^>]+\s*>|\[pagebreak\]|__XT_[A-Za-z0-9_]+__",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    partial void OnEntryFilterTextChanged(string value) => EntriesView.Refresh();

    partial void OnEntryFilterStatusChanged(string value) => EntriesView.Refresh();

    partial void OnEntryFilterTagsOnlyChanged(bool value)
    {
        if (!value && EntryFilterTagMismatchOnly)
        {
            EntryFilterTagMismatchOnly = false;
            return;
        }

        EntriesView.Refresh();
    }

    partial void OnEntryFilterTagMismatchOnlyChanged(bool value)
    {
        if (value && !EntryFilterTagsOnly)
        {
            EntryFilterTagsOnly = true;
            return;
        }

        EntriesView.Refresh();
    }

    private bool EntryFilter(object obj)
    {
        if (obj is not StringEntryViewModel entry)
        {
            return true;
        }

        var statusFilter = (EntryFilterStatus ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != EntryStatusAll)
        {
            if (statusFilter == EntryStatusNeedsReview)
            {
                if (entry.Status != StringEntryStatus.Pending
                    && entry.Status != StringEntryStatus.Error
                    && entry.Status != StringEntryStatus.Edited)
                {
                    return false;
                }
            }
            else if (Enum.TryParse<StringEntryStatus>(statusFilter, ignoreCase: true, out var status))
            {
                if (entry.Status != status)
                {
                    return false;
                }
            }
        }

        var q = (EntryFilterText ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(q) && !MatchesEntryQuery(entry, q))
        {
            return false;
        }

        if (EntryFilterTagMismatchOnly)
        {
            return HasTokenMismatch(entry.SourceText, entry.DestText);
        }

        if (EntryFilterTagsOnly)
        {
            return HasAnyUiTags(entry.SourceText);
        }

        return true;
    }

    private static bool MatchesEntryQuery(StringEntryViewModel entry, string q)
    {
        return ContainsIgnoreCase(entry.OrderIndex.ToString(), q)
               || ContainsIgnoreCase(entry.Id.ToString(), q)
               || ContainsIgnoreCase(entry.Edid ?? "", q)
               || ContainsIgnoreCase(entry.Rec ?? "", q)
               || ContainsIgnoreCase(entry.SourceText, q)
               || ContainsIgnoreCase(entry.DestText, q)
               || ContainsIgnoreCase(entry.ErrorMessage ?? "", q);
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool HasAnyUiTags(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var lt = text.IndexOf('<');
        if (lt >= 0 && text.IndexOf('>', lt + 1) > lt)
        {
            return true;
        }

        if (text.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return text.IndexOf("__XT_", StringComparison.Ordinal) >= 0;
    }

    private static bool HasTokenMismatch(string sourceText, string destText)
    {
        var sourceTokens = CollectUiTokens(sourceText);
        if (sourceTokens.Count == 0)
        {
            return false;
        }

        var destTokens = CollectUiTokens(destText);
        if (sourceTokens.Count != destTokens.Count)
        {
            return true;
        }

        foreach (var kvp in sourceTokens)
        {
            if (!destTokens.TryGetValue(kvp.Key, out var otherCount) || otherCount != kvp.Value)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, int> CollectUiTokens(string text)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return dict;
        }

        foreach (Match m in UiTagTokenRegex.Matches(text))
        {
            var raw = m.Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var token = NormalizeUiToken(raw);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (dict.TryGetValue(token, out var count))
            {
                dict[token] = count + 1;
            }
            else
            {
                dict[token] = 1;
            }
        }

        return dict;
    }

    private static string NormalizeUiToken(string token)
    {
        var s = token.Trim();
        if (s.Length == 0)
        {
            return "";
        }

        if (s[0] == '<')
        {
            s = Regex.Replace(s, @"\s+", "", RegexOptions.CultureInvariant);
            return s.ToLowerInvariant();
        }

        if (s[0] == '[')
        {
            return s.ToLowerInvariant();
        }

        if (s.StartsWith("__XT_", StringComparison.OrdinalIgnoreCase))
        {
            return s.ToUpperInvariant();
        }

        return s;
    }
}
