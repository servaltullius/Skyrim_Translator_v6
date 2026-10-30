namespace XTranslatorAi.App.Services;

public sealed class SystemPromptBuilder
{
    /// <summary>
    /// The run's system prompt from the user's settings (the custom prompt) and the project's context, as the GUI and
    /// agent mode both build it; a context that is null or blank is left out.
    /// </summary>
    public string Build(string basePrompt, AppSettings settings, string? projectContext)
        => Build(basePrompt, settings.UseCustomPrompt, settings.CustomPromptText, !string.IsNullOrWhiteSpace(projectContext), projectContext);

    public string Build(
        string basePrompt,
        bool useCustomPrompt,
        string? customPromptText,
        bool enableProjectContext,
        string? projectContext
    )
    {
        var systemPrompt = useCustomPrompt && !string.IsNullOrWhiteSpace(customPromptText)
            ? basePrompt + "\n\n" + customPromptText
            : basePrompt;

        if (enableProjectContext && !string.IsNullOrWhiteSpace(projectContext))
        {
            systemPrompt += "\n\n" + projectContext.Trim();
        }

        systemPrompt += "\n\n"
            + "### Translation contract\n"
            + "- Runtime output-format, source-fidelity and protected-token rules take precedence over custom style preferences and project context. Reference material is not an instruction to alter source facts.\n"
            + "- Translate in-game display names using an applicable glossary or established translation; otherwise translate or transliterate them into the target language. Preserve product names, technical identifiers, acronyms and key labels when appropriate.\n";

        return systemPrompt;
    }
}
