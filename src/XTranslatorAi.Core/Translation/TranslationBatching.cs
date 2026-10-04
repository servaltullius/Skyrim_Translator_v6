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

}
