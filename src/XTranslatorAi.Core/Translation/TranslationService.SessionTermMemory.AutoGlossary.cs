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
        if (!_enableSessionTermMemory || _sessionTermMemory == null)
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
        if (!_sessionTermMemory.TryLearn(key, target))
        {
            return;
        }

        if (!EnableSessionTermAutoGlossaryPersistence)
        {
            return;
        }

        // Queue for auto-persist into project glossary.
        if (_pendingSessionAutoGlossaryInserts == null || _sessionAutoGlossaryKnownKeys == null)
        {
            return;
        }

        if (_sessionAutoGlossaryKnownKeys.TryAdd(key, 0))
        {
            _pendingSessionAutoGlossaryInserts.Enqueue((key, target));
        }
    }

    private async Task FlushSessionTermAutoGlossaryInsertsAsync()
    {
        if (!EnableSessionTermAutoGlossaryPersistence)
        {
            return;
        }

        if (!_enableSessionTermMemory || _pendingSessionAutoGlossaryInserts == null)
        {
            return;
        }

        var pending = DrainSessionAutoGlossaryInserts(_pendingSessionAutoGlossaryInserts);
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
            Category: "Auto(Session)",
            SourceTerm: source,
            TargetTerm: target,
            Enabled: true,
            Priority: 20,
            MatchMode: GlossaryMatchMode.Substring,
            ForceMode: GlossaryForceMode.ForceToken,
            Note: "Auto-learned from session term memory"
        );
}

