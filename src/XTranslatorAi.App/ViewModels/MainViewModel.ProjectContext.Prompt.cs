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
        // Line-by-line translation does not know who speaks to whom; Serana Dialogue Add-On switched between
        // 반말 and 해요체 in 530 of its lines. An explicit rule keeps each relationship in one speech level.
        sb.AppendLine("  5) Speech levels (말투), only when dialogue (INFO/DIAL) is a large part of the report: one line per relationship");
        sb.AppendLine("     between the main speakers and the player, such as \"세라나 → 플레이어: 반말\" and \"플레이어 → 세라나: 반말\".");
        sb.AppendLine("     Decide from the samples and the summary: companions, lovers and close friends usually use 반말 both ways;");
        sb.AppendLine("     servants, merchants and guards use 존댓말 (해요체 or 하오체) to the player. Also give the level the main speaker");
        sb.AppendLine("     uses with strangers or superiors when the samples show such scenes, such as \"세라나 → 처음 만나는 NPC: 해요체\".");
        sb.AppendLine("     Give one level per relationship, never alternatives.");
        sb.AppendLine("- Use only information present in the report; do not invent lore facts.");
        // The translation follows this list over its other references, so a guessed entry overrides
        // the established name (a generated "Deathblow: 치명타" beat the earlier patch's 치명적 일격).
        sb.AppendLine("- Terminology entries need evidence in the report:");
        sb.AppendLine("  - If a term has \"target\", use that target exactly.");
        sb.AppendLine("  - If it has \"previousTranslations\" (names from the earlier translated release), use the Korean those examples consistently use for the term.");
        sb.AppendLine("  - If it has neither, leave it out of the terminology list. Never guess a translation.");
        sb.AppendLine("  - Give exactly one target per term. Never list alternatives such as \"A / B\"; if the evidence disagrees, leave the term out.");
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
