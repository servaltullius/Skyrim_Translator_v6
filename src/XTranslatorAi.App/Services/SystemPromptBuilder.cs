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
            + "- Do not leave translatable source text untranslated. In-game proper nouns (characters, places, races, items, skills) MUST be transliterated into the target language, never left in English. Only external tool/mod product names (e.g. SkyUI, QuickLoot IE) may remain as-is.\n";

        return systemPrompt;
    }
}
