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
