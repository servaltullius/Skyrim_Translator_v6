using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private void TryLearnSessionTermMemory(long id, string sourceText, string translatedText)
    {
        Ctx.RunNames?.Learn(sourceText, translatedText);

        if (!Ctx.EnableSessionTermMemory || Ctx.SessionTermMemory == null)
        {
            return;
        }

        var rec = GetRecForId(id);
        if (!IsSessionTermRec(rec))
        {
            return;
        }

        if (!IsSessionTermDefinitionText(sourceText) || !IsSessionTermTranslationText(translatedText))
        {
            return;
        }

        var key = NormalizeSessionTermKey(sourceText);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var target = translatedText.Trim();
        if (!Ctx.SessionTermMemory.TryLearn(key, target, allowForce: false))
        {
            return;
        }

        if (!EnableSessionTermAutoGlossaryPersistence)
        {
            return;
        }

        // Queue for auto-persist into project glossary.
        if (Ctx.PendingSessionAutoGlossaryInserts == null || Ctx.SessionAutoGlossaryKnownKeys == null)
        {
            return;
        }

        if (Ctx.SessionAutoGlossaryKnownKeys.TryAdd(key, 0))
        {
            Ctx.PendingSessionAutoGlossaryInserts.Enqueue((key, target));
        }
    }

    internal async Task FlushSessionTermAutoGlossaryInsertsAsync()
    {
        if (!EnableSessionTermAutoGlossaryPersistence || _ctx == null)
        {
            return;
        }

        if (!Ctx.EnableSessionTermMemory || Ctx.PendingSessionAutoGlossaryInserts == null)
        {
            return;
        }

        var pending = DrainSessionAutoGlossaryInserts(Ctx.PendingSessionAutoGlossaryInserts);
        if (pending.Count == 0)
        {
            return;
        }

        await PersistSessionAutoGlossaryInsertsAsync(pending);
    }

    private static List<(string Source, string Target)> DrainSessionAutoGlossaryInserts(
        ConcurrentQueue<(string Source, string Target)> queue
    )
    {
        var pending = new List<(string Source, string Target)>();
        while (queue.TryDequeue(out var it))
        {
            if (string.IsNullOrWhiteSpace(it.Source) || string.IsNullOrWhiteSpace(it.Target))
            {
                continue;
            }

            pending.Add(it);
        }

        return pending;
    }

    private async Task PersistSessionAutoGlossaryInsertsAsync(IReadOnlyList<(string Source, string Target)> pending)
    {
        foreach (var (source, target) in pending)
        {
            // A conflict may have arrived before this queued suggestion is flushed.
            if (Ctx.SessionTermMemory?.IsConflicted(source) == true) continue;
            try
            {
                await _db.TryInsertGlossaryIfMissingAsync(
                    request: CreateSessionAutoGlossaryRequest(source, target),
                    cancellationToken: CancellationToken.None
                );
            }
            catch
            {
                // Don't fail translation on glossary persistence issues.
            }
        }
    }

    private static GlossaryUpsertRequest CreateSessionAutoGlossaryRequest(string source, string target)
        => new(
            Category: GlossaryMerger.SessionAutoSuggestionCategory,
            SourceTerm: source,
            TargetTerm: target,
            Enabled: false,
            Priority: 20,
            MatchMode: GlossaryMatchMode.WordBoundary,
            ForceMode: GlossaryForceMode.PromptOnly,
            Note: "Auto-learned suggestion; review before enabling"
        );
}
