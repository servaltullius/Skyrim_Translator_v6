using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.Core.Translation;

public static class TranslationBatching
{
    /// <param name="groupOf">
    /// The group of each item, consecutive in <paramref name="items"/>; a group that fits in one batch is not split
    /// across two. Elden Rim's "Learning - Flame of Lion" and "Rim Scroll - Flame of Lion" were sorted together but cut
    /// apart at a batch boundary, and two requests in flight at once named the skill 사자의 화염 and 사자의 불꽃.
    /// An empty group keeps nothing together.
    /// </param>
    public static IEnumerable<IReadOnlyList<T>> ChunkBy<T>(
        IReadOnlyList<T> items,
        Func<T, int> weightSelector,
        int maxItems,
        int maxWeight,
        Func<T, string>? groupOf = null
    )
    {
        var batch = new List<T>(capacity: Math.Max(1, maxItems));
        var weight = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var w = weightSelector(item);
            var full = batch.Count >= maxItems || weight + w > maxWeight;
            if (batch.Count > 0 && !full && groupOf != null && StartsGroup(items, i, groupOf, out var group))
            {
                // The whole group would not fit after what the batch holds, but fits in a batch of its own.
                var (count, groupWeight) = Measure(items, i, group, groupOf, weightSelector);
                full = (batch.Count + count > maxItems || weight + groupWeight > maxWeight)
                       && count <= maxItems && groupWeight <= maxWeight;
            }

            if (batch.Count > 0 && full)
            {
                yield return batch.ToList();
                batch.Clear();
                weight = 0;
            }

            batch.Add(item);
            weight += w;
        }

        if (batch.Count > 0)
        {
            yield return batch.ToList();
        }
    }

    private static bool StartsGroup<T>(IReadOnlyList<T> items, int index, Func<T, string> groupOf, out string group)
    {
        group = groupOf(items[index]) ?? "";
        return group.Length > 0 && (index == 0 || !string.Equals(groupOf(items[index - 1]), group, StringComparison.OrdinalIgnoreCase));
    }

    private static (int Count, int Weight) Measure<T>(IReadOnlyList<T> items, int start, string group, Func<T, string> groupOf,
        Func<T, int> weightSelector)
    {
        var count = 0;
        var weight = 0;
        for (var i = start; i < items.Count && string.Equals(groupOf(items[i]), group, StringComparison.OrdinalIgnoreCase); i++)
        {
            count++;
            weight += weightSelector(items[i]);
        }

        return (count, weight);
    }

}
