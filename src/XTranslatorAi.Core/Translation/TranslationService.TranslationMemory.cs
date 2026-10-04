using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
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

        // Project TM should override franchise TM when keys collide.
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

        var final = MatchSourceLineEndings(sourceText, tmText);
        if (Ctx.EnableTemplateFixer)
        {
            final = MagDurPlaceholderFixer.Fix(sourceText, final, targetLang);
        }
        final = PlaceholderUnitBinder.EnforceUnitsFromSource(targetLang, sourceText, final);
        final = KoreanProtectFromFixer.Fix(targetLang, sourceText, final);
        final = KoreanTranslationFixer.Fix(targetLang, final);
        final = PercentSignFixer.FixDuplicatePercents(final, sourceText);
        try
        {
            TokenValidator.ValidateFinalTextIntegrity(sourceText, final, context: $"id={id} tm post-edits");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // If a TM entry breaks tag/placeholder integrity, skip it and fall back to LLM translation.
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            await _db.UpsertStringNoteAsync(id, TranslationConstants.TmFallbackNoteKind, $"TM 폴백: {detail}", cancellationToken);
            return false;
        }
        TryLearnSessionTermMemory(id, sourceText, final);

        await _db.UpdateStringTranslationAsync(id, final, StringEntryStatus.Done, null, cancellationToken);
        await _db.DeleteStringNoteAsync(id, TranslationConstants.TmFallbackNoteKind, cancellationToken);
        await _db.UpsertStringNoteAsync(id, TranslationConstants.TmHitNoteKind, "TM 적용", cancellationToken);
        if (onRowUpdated != null)
        {
            NotifyRowUpdated(onRowUpdated, id, StringEntryStatus.Done, final);
        }

        return true;
    }

    // The TM key folds CRLF, CR and LF together, so an entry saved from one row also matches the same source
    // written with another line-ending style. Line breaks are protected text, so the stored translation takes
    // the source's style; otherwise the integrity check rejects the hit and the row goes to the model.
    // A source that mixes styles keeps the stored text as it is.
    private static string MatchSourceLineEndings(string sourceText, string tmText)
    {
        var lineEnding = GetUniformLineEnding(sourceText);
        if (lineEnding == null || tmText.IndexOfAny(LineBreakChars) < 0)
        {
            return tmText;
        }

        var lf = tmText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return lineEnding == "\n" ? lf : lf.Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static readonly char[] LineBreakChars = { '\r', '\n' };

    private static string? GetUniformLineEnding(string text)
    {
        string? found = null;
        for (var i = 0; i < text.Length; i++)
        {
            string current;
            if (text[i] == '\r')
            {
                current = i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
                if (current.Length == 2)
                {
                    i++;
                }
            }
            else if (text[i] == '\n')
            {
                current = "\n";
            }
            else
            {
                continue;
            }

            if (found != null && !string.Equals(found, current, StringComparison.Ordinal))
            {
                return null;
            }

            found = current;
        }

        return found;
    }
}
