using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

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
        foreach (var (token, replacement) in glossary.TokenToReplacement)
        {
            if (text.Contains(token, StringComparison.Ordinal))
            {
                continue;
            }
            else if (!string.IsNullOrEmpty(replacement)
                     && text.Contains(replacement, StringComparison.Ordinal))
            {
                // Model dropped the token but already output the correct translation directly — accept as-is.
            }
            else
            {
                throw new InvalidOperationException($"Missing glossary token in translation: {token}");
            }
        }
        text = ReplaceGlossaryTokens(text, glossary.TokenToReplacement, LanguageHelper.IsKoreanLanguage(targetLang));

        var unmasked = masker.Unmask(text, masked.TokenToOriginal);
        unmasked = PlaceholderUnitBinder.ReplaceUnitsAfterUnmask(targetLang, unmasked);
        return PercentSignFixer.FixDuplicatePercents(unmasked, masker.Unmask(masked.Text, masked.TokenToOriginal));
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
                if (Ctx.EnableTemplateFixer)
                {
                    dupFinal = MagDurPlaceholderFixer.Fix(dup.Source, dupFinal, targetLang);
                }
                dupFinal = PlaceholderUnitBinder.EnforceUnitsFromSource(targetLang, dup.Source, dupFinal);
                dupFinal = KoreanProtectFromFixer.Fix(targetLang, dup.Source, dupFinal);
                dupFinal = KoreanTranslationFixer.Fix(targetLang, dupFinal);
                TokenValidator.ValidateFinalTextIntegrity(dup.Source, dupFinal, context: $"id={dup.Id} post-edits");
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

    /// <summary>
    /// Replaces every occurrence of <paramref name="token"/> with <paramref name="replacement"/>,
    /// but removes the token (instead of doubling the text) when the replacement already
    /// appears immediately adjacent (before or after, with optional whitespace).
    /// </summary>
    internal static string ReplaceTokenDedupAdjacent(string text, string token, string replacement)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
        {
            return text;
        }

        if (string.IsNullOrEmpty(replacement))
        {
            return text.Replace(token, replacement, StringComparison.Ordinal);
        }

        var matches = new List<(int Position, string Token, string Replacement)>();
        var position = 0;
        while ((position = text.IndexOf(token, position, StringComparison.Ordinal)) >= 0)
        {
            matches.Add((position, token, replacement));
            position += token.Length;
        }
        return ReplaceTokenMatches(text, matches);
    }

    internal static string ReplaceGlossaryTokens(
        string text,
        IReadOnlyDictionary<string, string> replacements,
        bool fixKoreanParticles = false
    )
    {
        var matches = new List<(int Position, string Token, string Replacement)>();
        foreach (Match match in TranslationConstants.XtTokenRegex.Matches(text))
        {
            if (replacements.TryGetValue(match.Value, out var replacement))
            {
                matches.Add((match.Index, match.Value, replacement));
            }
        }
        return ReplaceTokenMatches(text, matches, fixKoreanParticles);
    }

    private static string ReplaceTokenMatches(
        string text,
        IReadOnlyList<(int Position, string Token, string Replacement)> matches,
        bool fixKoreanParticles = false
    )
    {
        // Inspect the original model output only. A replacement inserted by us
        // must never be mistaken for a duplicate next to the following token.
        var result = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var (pos, token, replacement) in matches)
        {
            if (pos < cursor)
            {
                continue;
            }
            var afterStart = pos + token.Length;

            if (replacement.Length > 0 && TryMatchDupAfterToken(text, afterStart, replacement, out var afterStripLen))
            {
                result.Append(text, cursor, pos - cursor).Append(replacement);
                cursor = afterStart + afterStripLen;
                continue;
            }

            if (replacement.Length > 0
                && TryMatchDupBeforeToken(text, pos, replacement, out var beforeStripLen)
                && pos - beforeStripLen >= cursor)
            {
                result.Append(text, cursor, pos - beforeStripLen - cursor).Append(replacement);
                cursor = afterStart;
                continue;
            }

            result.Append(text, cursor, pos - cursor).Append(replacement);
            cursor = afterStart;

            // The model chose this particle while seeing only the token, not the term.
            if (fixKoreanParticles
                && KoreanParticleSelector.TryFixParticleAfterTerm(replacement, text, afterStart, out var particle, out var particleLength))
            {
                result.Append(particle);
                cursor = afterStart + particleLength;
            }
        }
        return result.Append(text, cursor, text.Length - cursor).ToString();
    }

    private static bool TryMatchDupAfterToken(string text, int start, string replacement, out int stripLen)
    {
        stripLen = 0;
        var i = start;

        // Skip optional whitespace
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        // Match replacement
        if (i + replacement.Length > text.Length
            || string.Compare(text, i, replacement, 0, replacement.Length, StringComparison.Ordinal) != 0)
        {
            return false;
        }

        i += replacement.Length;

        // Strip optional trailing __
        if (i + 2 <= text.Length && text[i] == '_' && text[i + 1] == '_')
        {
            i += 2;
        }

        stripLen = i - start;
        return true;
    }

    private static bool TryMatchDupBeforeToken(string text, int tokenPos, string replacement, out int stripLen)
    {
        stripLen = 0;
        var i = tokenPos;

        // Skip trailing whitespace (backwards)
        while (i > 0 && char.IsWhiteSpace(text[i - 1]))
        {
            i--;
        }

        // Skip optional __ (backwards)
        if (i >= 2 && text[i - 1] == '_' && text[i - 2] == '_')
        {
            i -= 2;
        }

        // Match replacement (backwards)
        if (i < replacement.Length
            || string.Compare(text, i - replacement.Length, replacement, 0, replacement.Length, StringComparison.Ordinal) != 0)
        {
            return false;
        }

        stripLen = tokenPos - (i - replacement.Length);
        return true;
    }

    private sealed class SourceTargetComparer : IEqualityComparer<(string Source, string Target)>
    {
        public bool Equals((string Source, string Target) x, (string Source, string Target) y)
            => string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string Target) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target)
            );
    }
}
