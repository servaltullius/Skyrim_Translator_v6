using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private sealed record WorkQueues(
        ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> ShortQueue,
        ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> LongQueue,
        ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> VeryLongQueue,
        int ShortBatchesCount,
        int LongBatchesCount,
        bool IsGemini3
    );

    private async Task<WorkQueues> BuildWorkQueuesAsync(
        TranslateIdsRequest request,
        List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        int maxConcurrency
    )
    {
        var (shortItems, longItems, veryLongItems) = PartitionWorkItems(request, items);

        SortForBatchConsistency(shortItems);
        SortForBatchConsistency(longItems);
        await TryUpdateMaskedTokensPerCharHintAsync(request, veryLongItems);

        var shortBatches = BuildBatches(shortItems, request.BatchSize, request.MaxChars);
        var longBatchSize = Math.Max(1, Math.Min(request.BatchSize, 8));
        var longBatches = BuildBatches(longItems, longBatchSize, request.MaxChars);
        var veryLongBatches = BuildVeryLongBatches(veryLongItems);

        SortBatchesByWeight(shortBatches);
        SortBatchesByWeight(longBatches);
        SortBatchesByWeight(veryLongBatches);

        var shortQueue = new ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>(shortBatches);
        var longQueue = new ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>(longBatches);
        var veryLongQueue = new ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>(veryLongBatches);

        var isGemini3 = IsGemini3Model(request.ModelName);
        ConfigureVeryLongLaneGate(shortQueue, longQueue, veryLongQueue, maxConcurrency);

        return new WorkQueues(shortQueue, longQueue, veryLongQueue, shortBatches.Count, longBatches.Count, isGemini3);
    }

    private static (
        List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> ShortItems,
        List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> LongItems,
        List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> VeryLongItems
    ) PartitionWorkItems(
        TranslateIdsRequest request,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items
    )
    {
        var shortItems = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>();
        var longItems = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>();
        var veryLongItems = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>();

        var shortMax = Math.Clamp(request.MaxChars / 3, min: 1500, max: 6000);
        foreach (var it in items)
        {
            if (it.Masked.Length > request.MaxChars)
            {
                veryLongItems.Add(it);
            }
            else if (it.Masked.Length <= shortMax)
            {
                shortItems.Add(it);
            }
            else
            {
                longItems.Add(it);
            }
        }

        return (shortItems, longItems, veryLongItems);
    }

    private void SortForBatchConsistency(List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> list)
    {
        if (list.Count <= 1)
        {
            return;
        }

        var sorted = list
            .Select(
                (it, idx) =>
                    new
                    {
                        Item = it,
                        Index = idx,
                        Group = TranslationBatchGrouping.ComputeGroupKey(GetRecForId(it.Id), GetEdidForId(it.Id), it.Source),
                        Rec = (GetRecForId(it.Id) ?? "").Trim(),
                    }
            )
            .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Rec, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Item.Masked.Length)
            .ThenBy(x => x.Index)
            .Select(x => x.Item)
            .ToList();

        list.Clear();
        list.AddRange(sorted);
    }

    private async Task TryUpdateMaskedTokensPerCharHintAsync(
        TranslateIdsRequest request,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> veryLongItems
    )
    {
        if (veryLongItems.Count <= 0)
        {
            return;
        }

        try
        {
            var sampleText = veryLongItems[0].Masked;
            var sampleChars = Math.Min(sampleText.Length, 2000);
            if (sampleChars <= 0)
            {
                return;
            }

            var sample = sampleText.Substring(0, sampleChars);
            var tokens = await CountTokensWithGateAsync(request.ApiKey, request.ModelName, sample, request.CancellationToken);
            if (tokens > 0)
            {
                _maskedTokensPerCharHint = (double)tokens / sampleChars;
            }
        }
        catch
        {
            _maskedTokensPerCharHint = null;
        }
    }

    private static List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> BuildBatches(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        int batchSize,
        int maxChars
    )
    {
        var batches = new List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>();
        foreach (var batch in TranslationBatching.ChunkBy(items, it => it.Masked.Length, batchSize, maxChars))
        {
            batches.Add(batch);
        }
        return batches;
    }

    private static List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> BuildVeryLongBatches(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> veryLongItems
    )
    {
        var batches = new List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>();
        foreach (var it in veryLongItems)
        {
            batches.Add(new[] { it });
        }
        return batches;
    }

    private static void SortBatchesByWeight(
        List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> batches
    )
    {
        batches.Sort((a, b) => ComputeBatchWeight(a).CompareTo(ComputeBatchWeight(b)));
    }

    private void ConfigureVeryLongLaneGate(
        ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> shortQueue,
        ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> longQueue,
        ConcurrentQueue<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> veryLongQueue,
        int maxConcurrency
    )
    {
        // Create/clear lane gate now (used by TranslateTextWithSentinelAsync via RequestLane.VeryLong).
        _veryLongRequestGate?.Dispose();
        _veryLongRequestGate = null;
        if (veryLongQueue.IsEmpty || maxConcurrency < 3)
        {
            return;
        }

        var reservedShort = shortQueue.IsEmpty ? 0 : 1;
        var reservedLong = longQueue.IsEmpty ? 0 : 1;
        var reserved = Math.Min(reservedShort + reservedLong, maxConcurrency - 1);
        if (reserved <= 0)
        {
            return;
        }

        var veryLongLimit = Math.Max(1, maxConcurrency - reserved);
        _veryLongRequestGate = new SemaphoreSlim(veryLongLimit, maxConcurrency);
    }

    private static bool IsGemini3Model(string modelName)
    {
        var normalized = modelName.Trim();
        if (normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["models/".Length..];
        }

        return normalized.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);
    }
}
