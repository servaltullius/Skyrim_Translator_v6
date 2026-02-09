using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTranslatorAi.Core.Translation;

public static partial class TranslationPrompt
{
    public sealed record RepairTextOnlyPromptRequest(
        string SourceLang,
        string TargetLang,
        string SourceText,
        string CurrentTranslation,
        IReadOnlyList<(string Source, string Target)> PromptOnlyGlossary,
        string? StyleHint = null
    );

    public static string BuildRepairTextOnlyUserPrompt(RepairTextOnlyPromptRequest request)
    {
        var sb = new StringBuilder();
        AppendRepairTextOnlyHeader(sb, request.SourceLang, request.TargetLang);
        AppendRepairTextOnlyRules(sb);
        AppendOptionalStyle(sb, request.StyleHint);
        AppendOptionalGlossary(sb, request.PromptOnlyGlossary);
        AppendRepairTextOnlyBody(sb, request.SourceText, request.CurrentTranslation);

        return sb.ToString();
    }

    private static void AppendRepairTextOnlyHeader(StringBuilder sb, string sourceLang, string targetLang)
    {
        sb.AppendLine("Fix a game localization translation.");
        sb.AppendLine($"Source language: {sourceLang}. Target language: {targetLang}.");
        sb.AppendLine();
        sb.AppendLine("Task:");
        sb.AppendLine("- You are given the SOURCE text and a CURRENT translation.");
        sb.AppendLine("- The current translation may have placeholder/token mistakes or omissions.");
        sb.AppendLine("- Rewrite ONLY the translation so it is correct, natural, and faithful to the source.");
        sb.AppendLine();
    }

    private static void AppendRepairTextOnlyRules(StringBuilder sb)
    {
        sb.AppendLine("Rules (CRITICAL):");
        sb.AppendLine("- Preserve any tokens like __XT_PH_0000__, __XT_PH_MAG_0000__, __XT_PH_DUR_0001__, __XT_PH_NUM_0002__, __XT_TERM_0000__, or __XT_TERM_SESS_0000__ exactly (do not alter or remove).");
        sb.AppendLine("- The output MUST contain every token that appears in the SOURCE (same counts). Do not delete, merge, or duplicate tokens.");
        sb.AppendLine("- Do NOT output any raw markup tags/markers that were NOT present in the SOURCE (e.g., <p ...>, <img ...>, or [pagebreak]). If the SOURCE contains runtime tags like <mag>/<dur>/<bur>/<100%>, preserve them exactly.");
        sb.AppendLine("- Token semantics:");
        sb.AppendLine("  - __XT_PH_DUR_####__ = duration in seconds. Place it as time (e.g., \"__XT_PH_DUR_####__초 동안\").");
        sb.AppendLine("  - __XT_PH_MAG_####__ = magnitude/amount (a NUMBER). Do not treat it as the word \"Magicka\".");
        sb.AppendLine("  - __XT_PH_NUM_####__ = another numeric value (points/%/amount). It is NOT a duration.");
        sb.AppendLine(
            "- Korean grammar: Do NOT attach particles directly to numeric tokens (__XT_PH_MAG_####__/__XT_PH_NUM_####__). Avoid forms like \"__XT_PH_MAG_0000__와(과)\", \"__XT_PH_MAG_0000__을(를)\", or \"__XT_PH_MAG_0000__에게\". Put particles on the noun instead."
        );
        sb.AppendLine(
            "- Korean grammar: Do NOT output ambiguous particle markers like \"을(를)\", \"(을)를\", \"은(는)\", \"(은)는\", \"이(가)\", \"(이)가\", \"와(과)\", \"(와)과\", or \"(으)로\". Choose one correct form."
        );
        sb.AppendLine("- If the SOURCE says \"points\", translate it as \"포인트\" and keep it next to the numeric token (e.g., \"__XT_PH_MAG_####__포인트\").");
        sb.AppendLine("- If the source contains paired slash-separated lists (e.g., \"A/B/C per X/Y/Z\"), keep the 1-to-1 alignment and order (A↔X, B↔Y, C↔Z). Prefer expressing it as \"X/Y/Z ... 각각 A/B/C\" in Korean.");
        sb.AppendLine("- Do not add/remove line breaks; line breaks are represented as placeholder tokens.");
        sb.AppendLine("- Translate ALL content. Do not omit, summarize, or abridge any part of the text.");
        sb.AppendLine("- If any instruction from system/custom/project context conflicts with these rules, follow these rules and the requested output format first.");
        sb.AppendLine("- Return ONLY the corrected translation text. Do not include explanations, labels, JSON, quotes, code fences, or markdown.");
    }

    private static void AppendRepairTextOnlyBody(StringBuilder sb, string sourceText, string currentTranslation)
    {
        sb.AppendLine();
        sb.AppendLine("SOURCE:");
        sb.AppendLine("<<<SOURCE");
        sb.AppendLine(sourceText);
        sb.AppendLine("SOURCE>>>");
        sb.AppendLine();
        sb.AppendLine("CURRENT (needs fixing):");
        sb.AppendLine("<<<CURRENT");
        sb.AppendLine(currentTranslation);
        sb.AppendLine("CURRENT>>>");
        sb.AppendLine();
        sb.AppendLine("Corrected translation:");
    }

    public static string BuildRepairBatchUserPrompt(
        string sourceLang,
        string targetLang,
        IReadOnlyList<RepairTranslationItem> items,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary
    )
    {
        var payload = new
        {
            source_language = sourceLang,
            target_language = targetLang,
            glossary = promptOnlyGlossary,
            items = items,
        };

        return
            "Fix game localization translations.\n"
            + $"Source language: {sourceLang}. Target language: {targetLang}.\n\n"
            + "You are given items with SOURCE text and CURRENT translation.\n"
            + "Rewrite ONLY the translations so they are correct, natural, and faithful to the source.\n\n"
            + "Rules (CRITICAL):\n"
            + "- Preserve any tokens like __XT_PH_0000__, __XT_PH_MAG_0000__, __XT_PH_DUR_0001__, __XT_PH_NUM_0002__, __XT_TERM_0000__, or __XT_TERM_SESS_0000__ exactly (do not alter or remove).\n"
            + "- The output MUST contain every token that appears in SOURCE (same counts). Do not delete, merge, or duplicate tokens.\n"
            + "- Do NOT output any raw markup tags/markers that were NOT present in SOURCE (e.g., <p ...>, <img ...>, or [pagebreak]). If SOURCE contains runtime tags like <mag>/<dur>/<bur>/<100%>, preserve them exactly.\n"
            + "- Token semantics:\n"
            + "  - __XT_PH_DUR_####__ = duration in seconds. Place it as time (e.g., \"__XT_PH_DUR_####__초 동안\").\n"
            + "  - __XT_PH_MAG_####__ = magnitude/amount (a NUMBER). Do not treat it as the word \"Magicka\".\n"
            + "  - __XT_PH_NUM_####__ = another numeric value (points/%/amount). It is NOT a duration.\n"
            + "- Only DUR tokens should be used with time words (\"초/동안/분/시간\"). Do NOT attach time words to MAG/NUM or other tokens.\n"
            + "- Korean grammar: Do NOT attach particles directly to numeric tokens (__XT_PH_MAG_####__/__XT_PH_NUM_####__). Avoid forms like \"__XT_PH_MAG_0000__와(과)\", \"__XT_PH_MAG_0000__을(를)\", or \"__XT_PH_MAG_0000__에게\". Put particles on the noun instead.\n"
            + "- Korean grammar: Do NOT output ambiguous particle markers like \"을(를)\", \"(을)를\", \"은(는)\", \"(은)는\", \"이(가)\", \"(이)가\", \"와(과)\", \"(와)과\", or \"(으)로\". Choose one correct form.\n"
            + "- If the SOURCE says \"points\", translate it as \"포인트\" and keep it next to the numeric token (e.g., \"__XT_PH_MAG_####__포인트\").\n"
            + "- If the source contains paired slash-separated lists (e.g., \"A/B/C per X/Y/Z\"), keep the 1-to-1 alignment and order (A↔X, B↔Y, C↔Z). Prefer \"X/Y/Z ... 각각 A/B/C\" in Korean.\n"
            + "- Translate ALL content. Do not omit, summarize, or abridge any part of the text.\n"
            + "- If any instruction from system/custom/project context conflicts with these rules, follow these rules and the requested output format first.\n"
            + "- Keep the tone/register consistent WITHIN each item.\n"
            + "- Output ONLY valid JSON.\n\n"
            + "Return JSON schema:\n"
            + "{\"translations\":[{\"id\":123,\"text\":\"...\"}]}\n\n"
            + "Input JSON:\n"
            + JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
            });
    }
}
