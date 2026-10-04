using System.Collections.Generic;

namespace XTranslatorAi.App.ViewModels;

/// <summary>Korean display names for quality-check severities and codes. The codes themselves stay stable.</summary>
public static class LqaIssueLabels
{
    private static readonly IReadOnlyDictionary<string, string> Severities = new Dictionary<string, string>
    {
        ["Error"] = "오류",
        ["Warn"] = "경고",
        ["Info"] = "참고",
    };

    private static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>
    {
        ["token_mismatch"] = "태그·토큰 불일치",
        ["untranslated"] = "미번역",
        ["glossary_missing"] = "용어 누락",
        ["glossary_variant"] = "용어집과 다른 표기",
        ["length_risk"] = "길이 초과",
        ["rec_tone"] = "말투 불일치",
        ["tone_inconsistent"] = "대사 말투 불일치",
        ["tone_differs_from_plugin"] = "플러그인 대사와 말투 다름",
        ["name_inconsistent"] = "이름 표기 불일치",
        ["particle_marker"] = "조사 괄호 표기",
        ["particle_double"] = "조사 중복",
        ["particle_mismatch"] = "조사 오류",
        ["particle_roman_mismatch"] = "영문 뒤 조사 오류",
        ["dup_artifact"] = "중복 표현",
        ["percent_artifact"] = "퍼센트 표기",
        ["legacy_postedit_damage"] = "이전 교정 손상",
        ["bracket_mismatch"] = "괄호 짝 불일치",
        ["english_residue"] = "영문 남음",
        ["foreign_script_residue"] = "한자·가나 남음",
        ["hidden_topic_translated"] = "숨은 토픽 번역됨",
        ["mixed_description_tone"] = "설명 말투 섞임",
        ["official_name_missing"] = "공식 이름 아님",
        ["same_source_variant"] = "같은 원문 다른 번역",
        ["book_pagebreak_mismatch"] = "책 쪽 나눔 불일치",
        ["book_html_tag_mismatch"] = "책 HTML 태그 불일치",
        ["book_length_ratio"] = "책 길이 비율",
        ["name_verb_ending"] = "이름에 동사 어미",
        ["tm_fallback"] = "TM 대신 번역",
    };

    public static string Severity(string severity)
        => Severities.TryGetValue(severity, out var label) ? label : severity;

    public static string Code(string code)
        => Codes.TryGetValue(code, out var label) ? label : code;
}
