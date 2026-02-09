namespace XTranslatorAi.App.Services;

public sealed class SystemPromptBuilder
{
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
            + "### Final Priority Guard (CRITICAL)\n"
            + "- If any instruction from custom prompt/project context conflicts with runtime translation rules, prioritize runtime translation rules.\n"
            + "- Do not leave translatable source text untranslated unless it is a proper noun/product/mod name that should remain as-is.\n";

        return systemPrompt;
    }
}
