namespace XTranslatorAi.App.Services;

public static class BookModelRouting
{
    public static bool IsBody(string? rec)
    {
        var fields = rec?.Split(':', 2, StringSplitOptions.TrimEntries);
        return fields is { Length: 2 }
            && fields[0].Equals("BOOK", StringComparison.OrdinalIgnoreCase)
            && fields[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldOverride(string? rec, bool isTitle, bool titlesEnabled, bool bodiesEnabled)
        => (titlesEnabled && isTitle) || (bodiesEnabled && IsBody(rec));
}
