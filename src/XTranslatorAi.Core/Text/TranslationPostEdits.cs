using System;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Public entrypoint for safe, deterministic post-edits that do not require API calls.
/// Intended for re-running post-processing on already-translated strings.
/// </summary>
public static class TranslationPostEdits
{
    public static string Apply(string targetLang, string sourceText, string translatedText, bool enableTemplateFixer)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return translatedText;
        }

        var src = sourceText ?? "";
        // Text in a rune font is the source's, whatever the translation wrote there (see CipherFontSpans).
        var working = CipherFontSpans.RestoreFromSource(src, PromptLeakCleaner.StripLeakedPlaceholderInstructions(src, translatedText));

        // Keep the order aligned with the core translation pipeline:
        // 1) Placeholder/tag fixes
        // 2) Unit enforcement
        // 3) Korean particle cleanup
        // 4) Percent cleanup
        if (enableTemplateFixer)
        {
            working = MagDurPlaceholderFixer.Fix(src, working, targetLang);
        }

        working = PlaceholderUnitBinder.EnforceUnitsFromSource(targetLang, src, working);
        working = KoreanProtectFromFixer.Fix(targetLang, src, working);
        working = KoreanSourceAwareFixes.Apply(targetLang, src, working);
        working = KoreanTranslationFixer.Fix(targetLang, working);
        working = PercentSignFixer.FixDuplicatePercents(working, src);

        return working;
    }
}
