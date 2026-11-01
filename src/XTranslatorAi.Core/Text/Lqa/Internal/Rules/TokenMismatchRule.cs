using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class TokenMismatchRule
{
    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        if (!LqaScanner.HasTokenMismatch(sourceText, destText))
        {
            return;
        }

        // Only the text in a rune font differs: say so, since "a tag differs" pointed at nothing the user could see.
        var restored = CipherFontSpans.RestoreFromSource(sourceText, destText);
        var cipherOnly = !string.Equals(restored, destText, System.StringComparison.Ordinal) && !LqaScanner.HasTokenMismatch(sourceText, restored);
        // Hangul there shows as boxes; a comma or a space the patch changed in the English shows as runes all the same.
        var hangul = cipherOnly && CipherFontSpans.HasHangul(destText);
        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: cipherOnly && !hangul ? "Warn" : "Error",
                Code: cipherOnly ? "cipher_font_text" : "token_mismatch",
                Message: !cipherOnly
                    ? "원문/번역 보호 요소가 일치하지 않습니다: 태그·토큰(<...>, [pagebreak], __XT_*__), 변수(%s, {name}), 줄바꿈 또는 서식 순서를 확인하세요."
                    : hangul
                        ? "룬 문자 글꼴($DaedricFont 등) 안의 글이 원문과 다릅니다. 이 글꼴은 영어 글자를 데이드릭 문자처럼 보여 주고 한글이 없어 네모·빈 칸으로 보입니다. 원문 그대로 두세요(후처리 재적용·내보내기가 되돌립니다)."
                        : "룬 문자 글꼴($DaedricFont 등) 안의 영어가 원문과 조금 다릅니다(문장부호·띄어쓰기). 원문 그대로 두는 것이 맞습니다(후처리 재적용·내보내기가 되돌립니다).",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
