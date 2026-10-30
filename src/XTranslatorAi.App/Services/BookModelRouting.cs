using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services;

public static class BookModelRouting
{
    public static bool IsBody(string? rec) => TranslationRowRules.IsBookBody(rec);

    public static bool ShouldOverride(string? rec, bool isTitle, bool titlesEnabled, bool bodiesEnabled)
        => (titlesEnabled && isTitle) || (bodiesEnabled && IsBody(rec));
}
