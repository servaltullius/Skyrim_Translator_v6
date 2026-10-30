using System;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// A known translation (TM, the game's text, an earlier release) made ready to fill a row: the fixes a model's answer
/// gets, and the check that its tags and tokens are the source's. The translation run and agent mode's Korean release
/// fill both use it: the release fill wrote the release's text as it was, and a line whose tags no longer fit was
/// 완료 until the check or the export refused it, where the run hands such a line to the model.
/// </summary>
public static class KnownTranslationFill
{
    /// <summary>The text to fill the row with, or why the known translation does not fit it.</summary>
    public sealed record Prepared(string? Text, string? Failure);

    public static Prepared Prepare(long id, string sourceText, string knownText, string targetLang, bool enableTemplateFixer)
    {
        var final = TranslationService.MatchSourceLineEndings(sourceText, knownText);
        if (enableTemplateFixer)
        {
            final = MagDurPlaceholderFixer.Fix(sourceText, final, targetLang);
        }
        final = PlaceholderUnitBinder.EnforceUnitsFromSource(targetLang, sourceText, final);
        final = KoreanProtectFromFixer.Fix(targetLang, sourceText, final);
        final = KoreanTranslationFixer.Fix(targetLang, final);
        final = PercentSignFixer.FixDuplicatePercents(final, sourceText);
        // The TM is keyed without edge whitespace ("gold:"), so "Gold: " was filled with "골드:".
        final = TranslationService.RestoreSourceEdgeSpaces(sourceText, final);
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
            return new Prepared(null, string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message);
        }

        return new Prepared(final, null);
    }

    /// <summary>The tm_fallback note of a known translation set aside for a request.</summary>
    public static string FallbackNote(string knownKind, string failure)
    {
        var origin = knownKind switch
        {
            TranslationConstants.GameStringNoteKind => "게임 번역",
            TranslationConstants.PreviousReleaseNoteKind => "이전 번역",
            _ => "TM",
        };
        return $"{origin} 폴백: {failure}";
    }
}
