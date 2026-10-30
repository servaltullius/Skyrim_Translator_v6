namespace XTranslatorAi.App.Services;

public sealed partial class SystemPromptBuilder
{
    /// <summary>
    /// The run's system prompt from the user's settings (the custom prompt) and the project's context, as the GUI and
    /// agent mode both build it; a context that is null or blank is left out.
    /// </summary>
    public string Build(string basePrompt, AppSettings settings, string? projectContext)
        => Build(basePrompt, settings.UseCustomPrompt, settings.CustomPromptText, !string.IsNullOrWhiteSpace(projectContext), projectContext);
}
