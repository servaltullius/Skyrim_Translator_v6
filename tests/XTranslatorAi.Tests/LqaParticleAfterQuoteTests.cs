using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// A particle right after a closing quote or bracket certainly follows the quoted noun, yet the check only
/// looked at particles attached to Hangul: "'화이트골드 탑'가", "\"드래곤본\"는", "[…달인]와" were corrected in
/// review but never reported.
/// </summary>
public class LqaParticleAfterQuoteTests
{
    [Theory]
    [InlineData("'화이트골드 탑'가 무너졌어.", "탑'가 → 탑'이")]
    [InlineData("\"드래곤본\"는 누구지?", "드래곤본\"는 → 드래곤본\"은")]
    [InlineData("[검술의 달인]와 함께", "달인]와 → 달인]과")]
    [InlineData("“세라나”을 찾아.", "세라나”을 → 세라나”를")]
    public void WrongParticleAfterQuote_IsReported(string dest, string expected)
        => Assert.Equal(expected, LqaHeuristics.FindHangulParticleMismatchSuggestion(dest));

    [Theory]
    [InlineData("'화이트골드 탑'이 무너졌어.")]
    [InlineData("\"드래곤본\"은 누구지?")]
    [InlineData("\"가자\"라고 말했다.")]           // direct quotation: 라고 after any final sound
    [InlineData("\"안녕\"이라고 말했다.")]
    [InlineData("도끼(양손)를 들었다.")]           // a closing parenthesis is not checked
    [InlineData("'세라나'의 성")]
    public void CorrectOrUnrelated_IsNotReported(string dest)
        => Assert.Null(LqaHeuristics.FindHangulParticleMismatchSuggestion(dest));
}

/// <summary>
/// Copula forms after a glossary term: "최초의 드래곤본였다고", "델핀였다는" were corrected in review. After a final
/// consonant the copula keeps its 이 (이었, 이야, 이에요).
/// </summary>
public class LqaCopulaAfterTermTests
{
    private static readonly string[] Terms = { "드래곤본", "델핀", "세라나" };

    [Theory]
    [InlineData("최초의 드래곤본였다고 해.", "드래곤본였 → 드래곤본이었")]
    [InlineData("다름 아닌 델핀였다는 거야.", "델핀였 → 델핀이었")]
    [InlineData("네가 드래곤본야?", "드래곤본야 → 드래곤본이야")]
    [InlineData("그게 드래곤본예요.", "드래곤본예요 → 드래곤본이에요")]
    public void VowelCopulaAfterFinalConsonant_IsReported(string dest, string expected)
        => Assert.Equal(expected, LqaHeuristics.FindHangulParticleMismatchSuggestion(dest, Terms));

    [Theory]
    [InlineData("최초의 드래곤본이었다고 해.")]
    [InlineData("세라나였다.")]
    [InlineData("세라나야?")]
    [InlineData("세라나예요.")]
    public void CorrectForms_AreNotReported(string dest)
        => Assert.Null(LqaHeuristics.FindHangulParticleMismatchSuggestion(dest, Terms));
}
