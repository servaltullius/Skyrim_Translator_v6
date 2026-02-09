using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.Core.Translation;

public static class TranslationBatching
{
    public static IEnumerable<IReadOnlyList<T>> ChunkBy<T>(
        IReadOnlyList<T> items,
        Func<T, int> weightSelector,
        int maxItems,
        int maxWeight
    )
    {
        var batch = new List<T>(capacity: Math.Max(1, maxItems));
        var weight = 0;

        foreach (var item in items)
        {
            var w = weightSelector(item);
            if (batch.Count > 0 && (batch.Count >= maxItems || weight + w > maxWeight))
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

    public static IEnumerable<IReadOnlyList<T>> ChunkByGroup<T>(
        IReadOnlyList<T> items,
        Func<T, int> weightSelector,
        int maxItems,
        int maxWeight,
        Func<T, string> groupSelector,
        IEqualityComparer<string>? groupComparer = null
    )
    {
        groupComparer ??= StringComparer.OrdinalIgnoreCase;

        var batch = new List<T>(capacity: Math.Max(1, maxItems));
        var weight = 0;
        string? currentGroup = null;

        foreach (var item in items)
        {
            var w = weightSelector(item);
            var group = groupSelector(item) ?? "";

            var groupChanged = batch.Count > 0 && currentGroup != null && !groupComparer.Equals(group, currentGroup);
            if (groupChanged || (batch.Count > 0 && (batch.Count >= maxItems || weight + w > maxWeight)))
            {
                yield return batch.ToList();
                batch.Clear();
                weight = 0;
                currentGroup = null;
            }

            if (batch.Count == 0)
            {
                currentGroup = group;
            }

            batch.Add(item);
            weight += w;
        }

        if (batch.Count > 0)
        {
            yield return batch.ToList();
        }
    }
}
