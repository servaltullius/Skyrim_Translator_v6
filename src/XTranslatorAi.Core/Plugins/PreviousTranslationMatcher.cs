using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Plugins;

/// <summary>
/// Pairs the fields of the plugin being translated with the same fields of an earlier translated release
/// of it (field keys carry FormID, record, subrecord and occurrence). An earlier text is kept only when it
/// is actually a translation: not empty, not identical to the current source, and, for Korean, containing Hangul.
/// </summary>
/// <remarks>
/// Keys alone are not enough between two releases. A FormID's high byte is an index into that release's own
/// master list, so when v2 adds a master, 02000800 is v1's own record but a record of the new master in v2; the
/// FormIDs of the earlier release are mapped through the two master lists by file name. A record whose EditorID
/// differs between the releases is a different record. And repeated subrecords are paired by position, so when
/// v2 inserts a quest objective, message button or response line, every later one would get its neighbour's
/// translation; a subrecord type whose count differs within a record is not paired at all.
/// </remarks>
public static class PreviousTranslationMatcher
{
    /// <param name="ChangedRecord">Fields whose record has another EditorID, or another number of that subrecord, in the earlier release.</param>
    public sealed record Result(
        IReadOnlyDictionary<string, string> TextByFieldKey,
        int NotInPreviousRelease,
        int SameAsSource,
        int NotTranslated,
        int ChangedRecord = 0
    );

    /// <summary>Pairs by field key alone, for two releases with the same master list.</summary>
    public static Result Match(IReadOnlyList<PluginField> current, IReadOnlyList<PluginField> previous, bool requireHangul)
        => Match(current, Array.Empty<string>(), previous, Array.Empty<string>(), requireHangul);

    public static Result Match(
        IReadOnlyList<PluginField> current,
        IReadOnlyList<string> currentMasters,
        IReadOnlyList<PluginField> previous,
        IReadOnlyList<string> previousMasters,
        bool requireHangul)
    {
        var previousByKey = new Dictionary<string, PluginField>(StringComparer.Ordinal);
        var previousCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in previous)
        {
            // A record of a master the current release no longer has cannot be in it.
            if (TryMapKey(field.Key, previousMasters, currentMasters, out var key) && previousByKey.TryAdd(key, field))
            {
                Increment(previousCounts, SubrecordGroup(key));
            }
        }

        var currentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in current)
        {
            Increment(currentCounts, SubrecordGroup(field.Key));
        }

        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        int missing = 0, same = 0, untranslated = 0, changed = 0;
        foreach (var field in current)
        {
            if (!previousByKey.TryGetValue(field.Key, out var old) || string.IsNullOrWhiteSpace(old.SourceText))
            {
                missing++;
                continue;
            }

            var group = SubrecordGroup(field.Key);
            var otherEditorId = !string.IsNullOrEmpty(field.EditorId) && !string.IsNullOrEmpty(old.EditorId)
                && !string.Equals(field.EditorId, old.EditorId, StringComparison.OrdinalIgnoreCase);
            if (otherEditorId || currentCounts[group] != previousCounts.GetValueOrDefault(group))
            {
                changed++;
                continue;
            }

            var text = old.SourceText.Trim();
            if (string.Equals(text, field.SourceText.Trim(), StringComparison.Ordinal))
            {
                same++;
                continue;
            }

            if (requireHangul && !text.Any(KoreanSyllables.IsHangulSyllable))
            {
                untranslated++;
                continue;
            }

            matched[field.Key] = text;
        }

        return new Result(matched, missing, same, untranslated, changed);
    }

    /// <summary>
    /// Rewrites the FormID in an earlier release's field key ("QUST/02000800/0/NNAM/1") into the current
    /// release's numbering: the high byte names a master by position, or the plugin itself past the masters.
    /// </summary>
    internal static bool TryMapKey(string key, IReadOnlyList<string> fromMasters, IReadOnlyList<string> toMasters, out string mapped)
    {
        mapped = key;
        var start = key.IndexOf('/');
        var end = start < 0 ? -1 : key.IndexOf('/', start + 1);
        if (end < 0 || !uint.TryParse(key.AsSpan(start + 1, end - start - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var formId))
        {
            return true;
        }

        var index = (int)(formId >> 24);
        int target;
        if (index < fromMasters.Count)
        {
            target = IndexOf(toMasters, fromMasters[index]);
            if (target < 0)
            {
                return false;
            }
        }
        else
        {
            target = toMasters.Count + (index - fromMasters.Count);
            if (target > 0xFF)
            {
                return false;
            }
        }

        if (target != index)
        {
            var remapped = ((uint)target << 24) | (formId & 0x00FFFFFFu);
            mapped = key[..(start + 1)] + remapped.ToString("X8", CultureInfo.InvariantCulture) + key[end..];
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<string> masters, string fileName)
    {
        for (var i = 0; i < masters.Count; i++)
        {
            if (string.Equals(masters[i], fileName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The key without its subrecord ordinal: every occurrence of one subrecord type in one record.</summary>
    private static string SubrecordGroup(string key)
    {
        var last = key.LastIndexOf('/');
        return last < 0 ? key : key[..last];
    }

    private static void Increment(Dictionary<string, int> counts, string group)
        => counts[group] = counts.GetValueOrDefault(group) + 1;
}
