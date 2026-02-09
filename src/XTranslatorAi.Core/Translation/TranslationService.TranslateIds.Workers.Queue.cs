using System;
using System.Collections.Generic;
using System.Threading;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static bool TryDequeueBatch(
        WorkQueues queues,
        BatchPreference preference,
        out IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>? batch,
        out BatchSource source
    )
    {
        return preference switch
        {
            BatchPreference.ShortFirst => TryDequeueBatchInOrder(
                queues,
                out batch,
                out source,
                (BatchSource.Short, BatchSource.Long, BatchSource.VeryLong)
            ),
            BatchPreference.LongFirst => TryDequeueBatchInOrder(
                queues,
                out batch,
                out source,
                (BatchSource.Long, BatchSource.Short, BatchSource.VeryLong)
            ),
            BatchPreference.VeryLongFirst => TryDequeueBatchInOrder(
                queues,
                out batch,
                out source,
                (BatchSource.VeryLong, BatchSource.Short, BatchSource.Long)
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(preference), preference, null),
        };
    }

    private static bool TryDequeueBatchInOrder(
        WorkQueues queues,
        out IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>? batch,
        out BatchSource source,
        (BatchSource First, BatchSource Second, BatchSource Third) order
    )
    {
        if (TryDequeueBatchSource(queues, order.First, out batch))
        {
            source = order.First;
            return true;
        }

        if (TryDequeueBatchSource(queues, order.Second, out batch))
        {
            source = order.Second;
            return true;
        }

        if (TryDequeueBatchSource(queues, order.Third, out batch))
        {
            source = order.Third;
            return true;
        }

        batch = null;
        source = BatchSource.None;
        return false;
    }

    private static bool TryDequeueBatchSource(
        WorkQueues queues,
        BatchSource source,
        out IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>? batch
    )
    {
        batch = null;
        return source switch
        {
            BatchSource.Short => queues.ShortQueue.TryDequeue(out batch),
            BatchSource.Long => queues.LongQueue.TryDequeue(out batch),
            BatchSource.VeryLong => queues.VeryLongQueue.TryDequeue(out batch),
            _ => false,
        };
    }

    private void ReleaseReservedGateSlotsIfNeeded(WorkerRunContext ctx, BatchSource source)
    {
        if (_veryLongRequestGate == null)
        {
            return;
        }

        if (source == BatchSource.Short && ctx.ReservedShortGateSlots > 0 && ctx.ShortBatchesRemainingForGate > 0)
        {
            if (Interlocked.Decrement(ref ctx.ShortBatchesRemainingForGate) == 0)
            {
                _veryLongRequestGate.Release(ctx.ReservedShortGateSlots);
            }
            return;
        }

        if (source == BatchSource.Long && ctx.ReservedLongGateSlots > 0 && ctx.LongBatchesRemainingForGate > 0)
        {
            if (Interlocked.Decrement(ref ctx.LongBatchesRemainingForGate) == 0)
            {
                _veryLongRequestGate.Release(ctx.ReservedLongGateSlots);
            }
        }
    }
}
