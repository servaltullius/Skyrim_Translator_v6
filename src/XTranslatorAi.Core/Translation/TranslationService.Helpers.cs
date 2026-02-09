using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static void NotifyRowUpdated(
        Func<long, StringEntryStatus, string, Task> onRowUpdated,
        long id,
        StringEntryStatus status,
        string text
    )
    {
        try
        {
            var task = onRowUpdated(id, status, text);
            if (task.IsCompletedSuccessfully)
            {
                return;
            }

            _ = task.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }
        catch
        {
            // ignore
        }
    }

    private static int FindSplitIndexByWeight(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        if (batch.Count < 2)
        {
            return 1;
        }

        var total = 0;
        foreach (var it in batch)
        {
            total += it.Masked.Length;
        }

        var target = total / 2;
        var running = 0;
        for (var i = 0; i < batch.Count; i++)
        {
            running += batch[i].Masked.Length;
            if (running >= target)
            {
                var splitAt = i + 1;
                return splitAt >= 1 && splitAt < batch.Count ? splitAt : batch.Count / 2;
            }
        }

        return batch.Count / 2;
    }

    private static string ApplyTokensAndUnmask(
        string modelText,
        GlossaryApplication glossary,
        MaskedText masked,
        PlaceholderMasker masker,
        string targetLang
    )
    {
        var text = modelText;
        foreach (var token in glossary.TokenToReplacement.Keys)
        {
            if (!text.Contains(token, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Missing glossary token in translation: {token}");
            }
        }

        foreach (var (token, replacement) in glossary.TokenToReplacement)
        {
            text = text.Replace(token, replacement, StringComparison.Ordinal);
        }

        var unmasked = masker.Unmask(text, masked.TokenToOriginal);
        unmasked = PlaceholderUnitBinder.ReplaceUnitsAfterUnmask(targetLang, unmasked);
        return PercentSignFixer.FixDuplicatePercents(unmasked);
    }

    private async Task<List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>> BuildDuplicateDoneUpdatesAsync(
        long canonicalId,
        string rawText,
        GlossaryApplication glossary,
        PlaceholderMasker placeholderMasker,
        string targetLang,
        Func<long, StringEntryStatus, string, Task>? onRowUpdated,
        bool awaitNotifications,
        CancellationToken cancellationToken
    )
    {
        var duplicates = GetDuplicateRows(canonicalId);
        if (duplicates.Count == 0)
        {
            return new List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>(capacity: 0);
        }

        var doneUpdates = new List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>(capacity: duplicates.Count);
        foreach (var dup in duplicates)
        {
            try
            {
                var dupFinal = ApplyTokensAndUnmask(rawText, glossary, dup.Mask, placeholderMasker, targetLang);
                if (_enableTemplateFixer)
                {
                    dupFinal = MagDurPlaceholderFixer.Fix(dup.Source, dupFinal, targetLang);
                }
                dupFinal = PlaceholderUnitBinder.EnforceUnitsFromSource(targetLang, dup.Source, dupFinal);
                dupFinal = KoreanProtectFromFixer.Fix(targetLang, dup.Source, dupFinal);
                dupFinal = KoreanTranslationFixer.Fix(targetLang, dupFinal);
                ValidateFinalTextIntegrity(dup.Source, dupFinal, context: $"id={dup.Id} post-edits");
                doneUpdates.Add((dup.Id, dupFinal, StringEntryStatus.Done, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var msg = FormatError(ex);
                await _db.UpdateStringStatusAsync(dup.Id, StringEntryStatus.Error, msg, cancellationToken);
                if (onRowUpdated != null)
                {
                    if (awaitNotifications)
                    {
                        await onRowUpdated(dup.Id, StringEntryStatus.Error, msg);
                    }
                    else
                    {
                        NotifyRowUpdated(onRowUpdated, dup.Id, StringEntryStatus.Error, msg);
                    }
                }
            }
        }

        return doneUpdates;
    }

    private async Task HandleRowErrorAsync(
        long id,
        Exception ex,
        Func<long, StringEntryStatus, string, Task>? onRowUpdated,
        bool awaitNotifications,
        CancellationToken cancellationToken
    )
    {
        var msg = FormatError(ex);
        await _db.UpdateStringStatusAsync(id, StringEntryStatus.Error, msg, cancellationToken);

        if (onRowUpdated != null)
        {
            if (awaitNotifications)
            {
                await onRowUpdated(id, StringEntryStatus.Error, msg);
            }
            else
            {
                NotifyRowUpdated(onRowUpdated, id, StringEntryStatus.Error, msg);
            }
        }

        await UpdateDuplicateStatusesAsync(id, StringEntryStatus.Error, msg, onRowUpdated, cancellationToken);
    }

    private async Task<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)> GetRowContextByIdAsync(
        long id,
        CancellationToken cancellationToken
    )
    {
        return await _db.GetStringTranslationContextAsync(id, cancellationToken);
    }

    private static int ComputeBatchWeight(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var sum = 0;
        foreach (var it in batch)
        {
            sum += it.Masked.Length;
        }
        return sum;
    }

    private sealed class SourceTargetComparer : IEqualityComparer<(string Source, string Target)>
    {
        public bool Equals((string Source, string Target) x, (string Source, string Target) y)
            => string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string Target) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target ?? "")
            );
    }
}
