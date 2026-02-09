namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private static string TrimAndClamp(string text, int maxChars)
    {
        var trimmed = (text ?? "").Trim();
        if (maxChars <= 0 || trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        return trimmed.Substring(0, maxChars).TrimEnd();
    }
}

