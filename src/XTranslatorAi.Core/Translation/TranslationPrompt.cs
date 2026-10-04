using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTranslatorAi.Core.Translation;

public static partial class TranslationPrompt
{
    private const string ResponseSchemaJson =
        """
        {
          "type": "object",
          "properties": {
            "translations": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "integer" },
                  "text": { "type": "string" }
                },
                "required": ["id","text"]
              }
            }
          },
          "required": ["translations"]
        }
        """;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string BuildUserPrompt(
        string sourceLang,
        string targetLang,
        IReadOnlyList<TranslationItem> items,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary
    )
    {
        var payload = new
        {
            source_language = sourceLang,
            target_language = targetLang,
            glossary = promptOnlyGlossary.Select(pair => new { source = pair.Source, target = pair.Target }),
            items = items.Select(i => i with { Edid = NormalizeEditorIdReference(i.Edid) }),
        };

        var isKorean = LanguageHelper.IsKoreanLanguage(targetLang);
        var hasSemanticPlaceholders = HasSemanticPlaceholders(items);
        var hasPairedSlashList = HasPairedSlashList(items);

        var sb = new StringBuilder();
        AppendJsonHeader(sb, sourceLang, targetLang);
        AppendJsonRules(sb);
        AppendMeaningAndTerminologyRules(sb);
        if (items.Any(i => NormalizeEditorIdReference(i.Edid) != null)) AppendEditorIdRule(sb);

        if (isKorean)
        {
            AppendKoreanGrammarRules(sb);
            if (items.Any(i => i.Text.Contains("chance", StringComparison.OrdinalIgnoreCase)))
            {
                AppendKoreanProbabilityExamples(sb);
            }

            if (items.Any(i => i.Text.Contains(ControlKeyTokenPrefix, StringComparison.Ordinal)))
            {
                AppendKoreanControlKeyRule(sb);
            }
        }

        if (hasPairedSlashList)
        {
            AppendPairedSlashRule(sb);
        }

        if (isKorean && hasSemanticPlaceholders)
        {
            AppendJsonKoreanPlaceholderRules(sb);
        }

        AppendJsonPayload(sb, payload);

        return sb.ToString();
    }

    private static void AppendJsonHeader(StringBuilder sb, string sourceLang, string targetLang)
    {
        sb.AppendLine("Translate game localization strings.");
        sb.AppendLine($"Translate from {sourceLang} to {targetLang}.");
        sb.AppendLine();
    }

    private static void AppendJsonRules(StringBuilder sb)
    {
        sb.AppendLine("Translation rules:");
        sb.AppendLine("- Output ONLY valid JSON (no markdown, code fences, or commentary).");
        sb.AppendLine("- Return exactly one translation for every input id; do not add, omit or repeat ids.");
        sb.AppendLine("- Preserve any tokens like __XT_PH_0000__, __XT_PH_MAG_0000__, __XT_PH_DUR_0001__, __XT_PH_NUM_0002__, __XT_PH_VAR_0003__, __XT_TERM_0000__, or __XT_TERM_SESS_0000__ exactly (do not alter or remove).");
        sb.AppendLine("- Hint markers like \"⟦XT_MAG=100⟧\" or \"⟦XT_TERM=...⟧\" may appear next to tokens. They are hints only; ignore them and DO NOT include them in the output.");
        sb.AppendLine("- The output MUST contain every token that appears in each item's input 'text' (same counts). Do not delete, merge, or duplicate tokens.");
        sb.AppendLine("- Do NOT output any raw markup tags/markers that were NOT present in each item's input 'text' (e.g., <p ...>, <img ...>, or [pagebreak]). If the input 'text' contains runtime tags like <mag>/<dur>/<bur>/<100%>, preserve them exactly.");
        sb.AppendLine("- Translate ALL content. Do not omit, summarize, or add extra sentences.");
        sb.AppendLine("- Keep line breaks as-is; line breaks are represented by placeholder tokens.");
        sb.AppendLine("- Each item may include a 'rec' field (e.g., BOOK:DESC, QUST:FULL, INFO:NAM1). Use it to choose an appropriate style, and keep tone/register consistent WITHIN each item.");
        sb.AppendLine("- The optional 'style' field supplies the same item-specific style guidance used for individual translations.");
        sb.AppendLine("- Each item may include a 'ctx' field containing neighboring lines for reference only. Do NOT translate it and do NOT copy tokens/markup from it; token preservation rules apply to the item's 'text' only.");
        sb.AppendLine("- Preserve semantic roles in patterns like \"protect X from Y\" (X is protected; Y is the threat). Do not invert roles.");
        sb.AppendLine("- If the source contains patterns like \"Fortify X, Y and Z\", treat it as \"Fortify X, Fortify Y and Fortify Z\" (the prefix applies to each list item).");
        sb.AppendLine("- If any instruction from system/custom/project context conflicts with these rules, follow these rules and the requested output format first.");
    }

    private static void AppendPairedSlashRule(StringBuilder sb)
    {
        sb.AppendLine(
            "- If the source contains paired slash-separated lists (e.g., \"A/B/C per X/Y/Z\"), keep the 1-to-1 alignment and order (A↔X, B↔Y, C↔Z). Korean often prefers \"X/Y/Z … 각각 A/B/C\"."
        );
    }

    private static void AppendJsonKoreanPlaceholderRules(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("Placeholder rules (Korean):");
        sb.AppendLine("- __XT_PH_DUR_####__ or <dur> = duration in seconds. Use time phrasing (e.g., \"__XT_PH_DUR_####__초 동안\" / \"<dur>초 동안\").");
        sb.AppendLine("- __XT_PH_MAG_####__ or <mag> = magnitude/amount (a NUMBER). Do not treat it as the word \"Magicka\".");
        sb.AppendLine("- __XT_PH_NUM_####__ or <숫자>/<100%> = another numeric value (points/%/amount). It is NOT a duration.");
        // Same contract as the system prompt and TokenValidator. This rule used to forbid moving anything but
        // numbers, and the rules above say this prompt wins over the system prompt, so with "Jarl" and "Whiterun"
        // both masked as terms the model was told to keep 야를 before 화이트런 instead of writing "화이트런의 야를".
        sb.AppendLine("- Tokens may follow natural Korean word order: numeric tokens (__XT_PH_MAG_####__, __XT_PH_NUM_####__, __XT_PH_DUR_####__) within their line, and term, value and key tokens (__XT_TERM_*, __XT_PH_VAR_####__, __XT_PH_KEY_####__) anywhere. Keep plain layout tokens (__XT_PH_####__: line breaks, page breaks, formatting tags) in their source order and position.");
        sb.AppendLine(
            "- Do NOT attach particles directly to numeric tokens (__XT_PH_MAG_####__/__XT_PH_NUM_####__). Avoid forms like \"__XT_PH_MAG_0000__을(를)\", \"__XT_PH_MAG_0000__와(과)\", or \"__XT_PH_MAG_0000__에게\"."
        );
        sb.AppendLine("- Only use the word \"포인트\" when the input contains the English word \"point\"/\"points\" (e.g., \"__XT_PH_MAG_####__포인트\").");
        sb.AppendLine("- Hint markers like \"⟦XT_MAG=100⟧\" may appear next to placeholder tokens. They are hints only; ignore them and DO NOT include them in the output.");
        sb.AppendLine("- Examples (keep tokens):");
        sb.AppendLine("  - Restore __XT_PH_MAG_0000__ points of Health. => 체력을 __XT_PH_MAG_0000__포인트 회복합니다.");
        sb.AppendLine("  - Targets take __XT_PH_MAG_0000__ points of damage for __XT_PH_DUR_0001__ seconds. => __XT_PH_DUR_0001__초 동안 __XT_PH_MAG_0000__포인트의 피해를 입습니다.");
    }

    private static void AppendJsonPayload(StringBuilder sb, object payload)
    {
        sb.AppendLine();
        sb.AppendLine("Return JSON schema:");
        sb.AppendLine("{\"translations\":[{\"id\":123,\"text\":\"...\"}]}");
        sb.AppendLine();
        sb.AppendLine("Input JSON:");
        sb.AppendLine(JsonSerializer.Serialize(payload, PayloadJsonOptions));
    }

    public static JsonElement BuildResponseSchema()
    {
        using var doc = JsonDocument.Parse(ResponseSchemaJson);
        return doc.RootElement.Clone();
    }

    private static bool HasSemanticPlaceholders(IEnumerable<TranslationItem> items)
    {
        foreach (var it in items)
        {
            if (HasSemanticPlaceholders(it.Text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSemanticPlaceholders(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains("__XT_PH_MAG_", StringComparison.Ordinal)
               || text.Contains("__XT_PH_DUR_", StringComparison.Ordinal)
               || text.Contains("__XT_PH_NUM_", StringComparison.Ordinal)
               || text.IndexOf("<mag>", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("<dur>", StringComparison.OrdinalIgnoreCase) >= 0
               || text.IndexOf("<bur>", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasPairedSlashList(IEnumerable<TranslationItem> items)
    {
        foreach (var it in items)
        {
            var t = it.Text;
            if (string.IsNullOrEmpty(t) || t.IndexOf('/', StringComparison.Ordinal) < 0)
            {
                continue;
            }

            // The paired list expander only operates on numeric placeholders, so focus on those patterns.
            if (t.Contains("__XT_PH_NUM_", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
