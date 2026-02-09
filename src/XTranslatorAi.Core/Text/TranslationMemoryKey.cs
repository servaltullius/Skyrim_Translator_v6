using System;

namespace XTranslatorAi.Core.Text;

public static class TranslationMemoryKey
{
    public static string NormalizeLanguage(string lang)
    {
        return (lang ?? "").Trim().ToLowerInvariant();
    }

    public static string NormalizeSource(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return "";
        }

        var normalized = sourceText.Trim();
        normalized = normalized.Replace("\r\n", "\n", StringComparison.Ordinal);
        normalized = normalized.Replace("\r", "\n", StringComparison.Ordinal);
        return normalized.ToLowerInvariant();
    }
}

