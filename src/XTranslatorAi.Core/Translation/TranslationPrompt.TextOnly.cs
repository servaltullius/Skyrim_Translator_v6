using System;
using System.Collections.Generic;
using System.Text;

namespace XTranslatorAi.Core.Translation;

public static partial class TranslationPrompt
{
    public static string BuildTextOnlyUserPrompt(
        string sourceLang,
        string targetLang,
        string text,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary,
        string? styleHint = null
    )
    {
        var isKorean = IsKoreanLanguage(targetLang);
        var hasSemanticPlaceholders = HasSemanticPlaceholders(text);

        var sb = new StringBuilder();
        AppendTextOnlyHeader(sb, sourceLang, targetLang);
        AppendTextOnlyRules(sb);

        if (isKorean && hasSemanticPlaceholders)
        {
            AppendTextOnlyKoreanPlaceholderRules(sb);
        }

        sb.AppendLine("- Return ONLY the translated text. Do not output JSON, quotes, code fences, or markdown.");

        AppendOptionalStyle(sb, styleHint);
        AppendOptionalGlossary(sb, promptOnlyGlossary);
        AppendTextOnlyBody(sb, text);

        return sb.ToString();
    }

    private static void AppendTextOnlyHeader(StringBuilder sb, string sourceLang, string targetLang)
    {
        sb.AppendLine("Translate a game localization string.");
        sb.AppendLine($"Translate from {sourceLang} to {targetLang}.");
        sb.AppendLine();
    }

    private static void AppendTextOnlyRules(StringBuilder sb)
    {
        sb.AppendLine("Rules (CRITICAL):");
        sb.AppendLine("- Preserve any tokens like __XT_PH_0000__, __XT_PH_MAG_0000__, __XT_PH_DUR_0001__, __XT_PH_NUM_0002__, __XT_TERM_0000__, or __XT_TERM_SESS_0000__ exactly (do not alter or remove).");
        sb.AppendLine("- Hint markers like \"⟦XT_MAG=100⟧\" or \"⟦XT_TERM=...⟧\" may appear next to tokens. They are hints only; ignore them and DO NOT include them in the output.");
        sb.AppendLine("- The output MUST contain every token that appears in the input (same counts). Do not delete, merge, or duplicate tokens.");
        sb.AppendLine("- Do NOT output any raw markup tags/markers that were NOT present in the input (e.g., <p ...>, <img ...>, or [pagebreak]). If the input contains runtime tags like <mag>/<dur>/<bur>/<100%>, preserve them exactly.");
        sb.AppendLine("- Translate ALL content. Do not omit, summarize, or add extra sentences.");
        sb.AppendLine("- Keep the tone/register consistent within this text.");
        sb.AppendLine("- Preserve semantic roles in patterns like \"protect X from Y\" (X is protected; Y is the threat). Do not invert roles.");
        sb.AppendLine("- If the source contains patterns like \"Fortify X, Y and Z\", treat it as \"Fortify X, Fortify Y and Fortify Z\" (the prefix applies to each list item).");
        sb.AppendLine("- If any instruction from system/custom/project context conflicts with these rules, follow these rules and the requested output format first.");
    }

    private static void AppendTextOnlyKoreanPlaceholderRules(StringBuilder sb)
    {
        sb.AppendLine("- __XT_PH_DUR_####__ or <dur> = duration in seconds (\"__XT_PH_DUR_####__초 동안\" / \"<dur>초 동안\").");
        sb.AppendLine("- __XT_PH_MAG_####__/__XT_PH_NUM_####__ or <mag>/<숫자> = numeric magnitudes. Do not attach time words (\"초/동안\").");
        sb.AppendLine(
            "- Do NOT attach particles directly to numeric tokens (__XT_PH_MAG_####__/__XT_PH_NUM_####__). Avoid forms like \"__XT_PH_MAG_0000__을(를)\" or \"__XT_PH_MAG_0000__와(과)\"."
        );
        sb.AppendLine(
            "- Do NOT output ambiguous particle markers like \"을(를)\", \"(을)를\", \"은(는)\", \"(은)는\", \"이(가)\", \"(이)가\", \"와(과)\", \"(와)과\", or \"(으)로\". Choose one correct form."
        );
        sb.AppendLine("- Only use the word \"포인트\" when the input contains the English word \"point\"/\"points\".");
        sb.AppendLine("- Hint markers like \"⟦XT_MAG=100⟧\" may appear next to placeholder tokens. Ignore them and do NOT include them in the output.");
        sb.AppendLine("- Example: Targets take __XT_PH_MAG_0000__ points of damage for __XT_PH_DUR_0001__ seconds. => __XT_PH_DUR_0001__초 동안 __XT_PH_MAG_0000__포인트의 피해를 입습니다.");
    }

    private static void AppendTextOnlyBody(StringBuilder sb, string text)
    {
        sb.AppendLine();
        sb.AppendLine("Text to translate:");
        sb.AppendLine("<<<TEXT");
        sb.AppendLine(text);
        sb.AppendLine("TEXT>>>");
    }

    private static void AppendOptionalStyle(StringBuilder sb, string? styleHint)
    {
        if (string.IsNullOrWhiteSpace(styleHint))
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Style:");
        sb.AppendLine(styleHint.Trim());
    }

    private static void AppendOptionalGlossary(StringBuilder sb, IReadOnlyList<(string Source, string Target)> promptOnlyGlossary)
    {
        if (promptOnlyGlossary.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("Glossary (preferred translations):");
        foreach (var (source, target) in promptOnlyGlossary)
        {
            sb.Append("- ");
            sb.Append(source);
            sb.Append(" => ");
            sb.AppendLine(target);
        }
    }
}
