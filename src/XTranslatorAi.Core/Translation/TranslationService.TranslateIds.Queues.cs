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

        var shortGroups = SortForBatchConsistency(shortItems);
        var longGroups = SortForBatchConsistency(longItems);
        await TryUpdateMaskedTokensPerCharHintAsync(request, veryLongItems);

        var shortBatches = SeparateRiskyRows(BuildBatches(shortItems, request.BatchSize, request.MaxChars, shortGroups), request, shortGroups);
        var longBatchSize = TranslationLimits.LongBatchSize(request.BatchSize);
        var longBatches = SeparateRiskyRows(BuildBatches(longItems, longBatchSize, request.MaxChars, longGroups), request, longGroups);
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

        var shortMax = TranslationLimits.ShortRowMaxChars(request.MaxChars);
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

    // Sorts the rows so related names sit together, and returns each row's group for BuildBatches.
    private Dictionary<long, string> SortForBatchConsistency(List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> list)
    {
        if (list.Count <= 1)
        {
            return new Dictionary<long, string>();
        }

        var groups = TranslationBatchGrouping.ComputeGroupKeys(
            list.Select(it => (GetRecForId(it.Id), GetEdidForId(it.Id), it.Source)).ToList()
        );
        var sorted = list
            .Select(
                (it, idx) =>
                    new
                    {
                        Item = it,
                        Index = idx,
                        Group = groups[idx],
                        Rec = (GetRecForId(it.Id) ?? "").Trim(),
                    }
            )
            .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Rec, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Item.Masked.Length)
            .ThenBy(x => x.Index)
            .ToList();

        list.Clear();
        list.AddRange(sorted.Select(x => x.Item));
        var groupById = new Dictionary<long, string>();
        foreach (var x in sorted) groupById.TryAdd(x.Item.Id, x.Group);
        return groupById;
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
                Ctx.MaskedTokensPerCharHint = (double)tokens / sampleChars;
            }
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsCredentialError(ex))
        {
            throw;
        }
        catch
        {
            Ctx.MaskedTokensPerCharHint = null;
        }
    }

    private List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> BuildBatches(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        int batchSize,
        int maxChars,
        IReadOnlyDictionary<long, string> groups
    )
    {
        var batches = new List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>();
        foreach (var batch in TranslationBatching.ChunkBy(items,
                     it => it.Masked.Length + (Ctx.EnableAdaptiveOutputBudget ? 8 * TranslationConstants.XtTokenRegex.Matches(it.Masked).Count : 0),
                     batchSize, maxChars, it => groups.GetValueOrDefault(it.Id, "")))
        {
            batches.Add(batch);
        }
        return batches;
    }

    /// <summary>
    /// A batch with a risky sentence ("protects you from", "against") is asked for several candidates whole
    /// (GetBatchCandidateCount): one such row among twelve tripled the output of all twelve, and the cost estimate did not
    /// count it. With a model that gives candidates, the risky rows go in a batch of their own, with the rows of their group
    /// (<paramref name="groups"/>, kept in one batch by ChunkBy): a skill's description apart from its names had the names
    /// and the description translated by two requests at once, the split ChunkBy was made to avoid.
    /// </summary>
    private List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> SeparateRiskyRows(
        List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> batches,
        TranslateIdsRequest request,
        IReadOnlyDictionary<long, string>? groups
    )
    {
        if (!GeminiModelPolicy.SupportsMultipleCandidates(request.ModelName))
        {
            return batches;
        }

        var separated = new List<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>>(batches.Count);
        foreach (var batch in batches)
        {
            string GroupOf(long id) => groups?.GetValueOrDefault(id, "") ?? "";
            var riskyRows = batch.Where(it => GetRiskyCandidateCountForSource(request.TargetLang, it.Source) > 1).ToList();
            var riskyGroups = riskyRows.Select(it => GroupOf(it.Id)).Where(group => group.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var risky = batch.Where(it => riskyRows.Any(r => r.Id == it.Id) || riskyGroups.Contains(GroupOf(it.Id))).Select(it => it.Id).ToHashSet();
            if (risky.Count == 0 || risky.Count == batch.Count)
            {
                separated.Add(batch);
                continue;
            }

            separated.Add(batch.Where(it => !risky.Contains(it.Id)).ToList());
            separated.Add(batch.Where(it => risky.Contains(it.Id)).ToList());
        }

        return separated;
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
        Ctx.VeryLongRequestGate?.Dispose();
        Ctx.VeryLongRequestGate = null;
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
        Ctx.VeryLongRequestGate = new SemaphoreSlim(veryLongLimit, maxConcurrency);
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
