using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private const string TmFallbackNoteKind = "tm_fallback";
    private const string TmHitNoteKind = "tm_hit";

    private Task<IReadOnlyDictionary<string, string>> LoadTranslationMemoryAsync(
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken
    )
    {
        return _db.GetTranslationMemoryAsync(sourceLang, targetLang, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> MergeTranslationMemory(
        IReadOnlyDictionary<string, string>? globalTranslationMemory,
        IReadOnlyDictionary<string, string> projectTranslationMemory
    )
    {
        if (globalTranslationMemory == null || globalTranslationMemory.Count == 0)
        {
            return projectTranslationMemory;
        }

        if (projectTranslationMemory.Count == 0)
        {
            return globalTranslationMemory;
        }

        // Project TM should override global TM when keys collide.
        var merged = new Dictionary<string, string>(globalTranslationMemory.Count + projectTranslationMemory.Count, StringComparer.Ordinal);
        foreach (var (key, value) in globalTranslationMemory)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                merged[key] = value ?? "";
            }
        }

        foreach (var (key, value) in projectTranslationMemory)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                merged[key] = value ?? "";
            }
        }

        return merged;
    }

    private async Task<bool> TryApplyTranslationMemoryAsync(
        long id,
        string sourceText,
        string targetLang,
        IReadOnlyDictionary<string, string> translationMemory,
        Func<long, StringEntryStatus, string, Task>? onRowUpdated,
        CancellationToken cancellationToken
    )
    {
        var srcKey = TranslationMemoryKey.NormalizeSource(sourceText);
        if (string.IsNullOrWhiteSpace(srcKey))
        {
            return false;
        }

        if (!translationMemory.TryGetValue(srcKey, out var tmText) || string.IsNullOrWhiteSpace(tmText))
        {
            return false;
        }

        var final = tmText;
        if (_enableTemplateFixer)
        {
            final = MagDurPlaceholderFixer.Fix(sourceText, final, targetLang);
        }
        final = PlaceholderUnitBinder.EnforceUnitsFromSource(targetLang, sourceText, final);
        final = KoreanProtectFromFixer.Fix(targetLang, sourceText, final);
        final = KoreanTranslationFixer.Fix(targetLang, final);
        final = PercentSignFixer.FixDuplicatePercents(final);
        try
        {
            ValidateFinalTextIntegrity(sourceText, final, context: $"id={id} tm post-edits");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If a TM entry breaks tag/placeholder integrity, skip it and fall back to LLM translation.
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            await _db.UpsertStringNoteAsync(id, TmFallbackNoteKind, $"TM 폴백: {detail}", cancellationToken);
            return false;
        }
        TryLearnSessionTermMemory(id, sourceText, final);

        await _db.UpdateStringTranslationAsync(id, final, StringEntryStatus.Done, null, cancellationToken);
        await _db.DeleteStringNoteAsync(id, TmFallbackNoteKind, cancellationToken);
        await _db.UpsertStringNoteAsync(id, TmHitNoteKind, "TM 적용", cancellationToken);
        if (onRowUpdated != null)
        {
            NotifyRowUpdated(onRowUpdated, id, StringEntryStatus.Done, final);
        }

        return true;
    }
}
