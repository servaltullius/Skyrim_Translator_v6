using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task RunWorkersAsync(
        TranslateIdsRequest request,
        PromptCache? promptCache,
        PlaceholderMasker placeholderMasker,
        System.Text.Json.JsonElement responseSchema,
        WorkQueues queues
    )
    {
        var maxConcurrency = Math.Max(1, request.MaxConcurrency);

        var hasShort = !queues.ShortQueue.IsEmpty;
        var hasLong = !queues.LongQueue.IsEmpty;
        var hasVeryLong = !queues.VeryLongQueue.IsEmpty;

        var reservedShortGateSlots = (_veryLongRequestGate != null && hasShort) ? 1 : 0;
        var reservedLongGateSlots = (_veryLongRequestGate != null && hasLong) ? 1 : 0;

        var (shortWorkers, longWorkers, veryLongWorkers) = ComputeWorkerAllocation(
            hasShort,
            hasLong,
            hasVeryLong,
            maxConcurrency,
            queues.IsGemini3
        );

        var run = new WorkerRunContext(
            request,
            promptCache,
            placeholderMasker,
            responseSchema,
            queues,
            reservedShortGateSlots,
            reservedLongGateSlots
        );

        var workers = new List<Task>(capacity: maxConcurrency);
        AddWorkers(workers, shortWorkers, run, BatchPreference.ShortFirst);
        AddWorkers(workers, longWorkers, run, BatchPreference.LongFirst);
        AddWorkers(workers, veryLongWorkers, run, BatchPreference.VeryLongFirst);

        await Task.WhenAll(workers);
    }

    private void AddWorkers(List<Task> workers, int count, WorkerRunContext run, BatchPreference preference)
    {
        for (var i = 0; i < count; i++)
        {
            workers.Add(WorkerAsync(run, preference));
        }
    }

    private sealed class WorkerRunContext
    {
        public TranslateIdsRequest Request { get; }
        public PromptCache? PromptCache { get; }
        public PlaceholderMasker PlaceholderMasker { get; }
        public System.Text.Json.JsonElement ResponseSchema { get; }
        public WorkQueues Queues { get; }

        public int ReservedShortGateSlots { get; }
        public int ReservedLongGateSlots { get; }

        public int ShortBatchesRemainingForGate;
        public int LongBatchesRemainingForGate;

        public WorkerRunContext(
            TranslateIdsRequest request,
            PromptCache? promptCache,
            PlaceholderMasker placeholderMasker,
            System.Text.Json.JsonElement responseSchema,
            WorkQueues queues,
            int reservedShortGateSlots,
            int reservedLongGateSlots
        )
        {
            Request = request;
            PromptCache = promptCache;
            PlaceholderMasker = placeholderMasker;
            ResponseSchema = responseSchema;
            Queues = queues;
            ReservedShortGateSlots = reservedShortGateSlots;
            ReservedLongGateSlots = reservedLongGateSlots;
            ShortBatchesRemainingForGate = queues.ShortBatchesCount;
            LongBatchesRemainingForGate = queues.LongBatchesCount;
        }
    }

    private static (int ShortWorkers, int LongWorkers, int VeryLongWorkers) ComputeWorkerAllocation(
        bool hasShort,
        bool hasLong,
        bool hasVeryLong,
        int maxConcurrency,
        bool isGemini3
    )
    {
        var shortWorkers = 0;
        var longWorkers = 0;
        var veryLongWorkers = 0;

        if (!hasVeryLong)
        {
            if (hasShort && hasLong && maxConcurrency >= 2)
            {
                longWorkers = 1;
                shortWorkers = maxConcurrency - longWorkers;
            }
            else if (hasShort)
            {
                shortWorkers = maxConcurrency;
            }
            else
            {
                longWorkers = maxConcurrency;
            }
        }
        else
        {
            shortWorkers = hasShort ? (maxConcurrency >= 5 ? 2 : 1) : 0;
            if (isGemini3 && hasShort && maxConcurrency >= 5)
            {
                shortWorkers = 1;
            }

            if (hasLong && (maxConcurrency - shortWorkers) >= 2)
            {
                longWorkers = 1;
            }
            veryLongWorkers = Math.Max(0, maxConcurrency - shortWorkers - longWorkers);
        }

        return (shortWorkers, longWorkers, veryLongWorkers);
    }
}
