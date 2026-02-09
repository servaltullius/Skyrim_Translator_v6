using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using XTranslatorAi.Core.Text.ProjectContext;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private const string ProjectContextResponseSchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "context": { "type": "string" }
          },
          "required": ["context"]
        }
        """;

    private static readonly JsonSerializerOptions ProjectContextJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static string BuildProjectContextUserPrompt(ProjectContextScanReport report, string? translationSystemPrompt)
    {
        var json = JsonSerializer.Serialize(report, ProjectContextJsonOptions);

        var sb = new StringBuilder();
        sb.AppendLine("You are given a JSON 'project scan report'.");
        sb.AppendLine("Write a compact 'Project Context' text block to append to a system prompt for Korean localization.");
        sb.AppendLine();
        sb.AppendLine("Requirements:");
        sb.AppendLine("- Output ONLY JSON: {\"context\":\"...\"}");
        sb.AppendLine("- Write the context in Korean (with English terms preserved where needed).");
        sb.AppendLine("- Keep it short (roughly <= 2000 Korean characters).");
        sb.AppendLine("- Include sections:");
        sb.AppendLine("  1) Mod/Addon summary (1-3 lines)");
        sb.AppendLine("  2) Terminology (source => target) for the most important terms");
        sb.AppendLine("  3) Style rules (especially for effect/perk descriptions)");
        sb.AppendLine("  4) Numeric/template conventions (duration/cooldown/percent/points phrasing)");
        sb.AppendLine("- Use only information present in the report; do not invent lore facts.");
        sb.AppendLine();
        sb.AppendLine("Project scan report JSON:");
        sb.AppendLine(json);

        if (!string.IsNullOrWhiteSpace(translationSystemPrompt))
        {
            sb.AppendLine();
            sb.AppendLine("Translation system prompt that will be used later (REFERENCE ONLY):");
            sb.AppendLine("- Use it to align terminology/style with the actual translation rules.");
            sb.AppendLine("- Do NOT copy it verbatim into the output.");
            sb.AppendLine("- Output must still follow this task's requirement: ONLY JSON {\"context\":\"...\"}");
            sb.AppendLine("<<<PROMPT");
            sb.AppendLine(translationSystemPrompt.Trim());
            sb.AppendLine("PROMPT>>>");
        }

        return sb.ToString();
    }

    private static JsonElement BuildProjectContextResponseSchema()
    {
        using var doc = JsonDocument.Parse(ProjectContextResponseSchemaJson);
        return doc.RootElement.Clone();
    }
}
