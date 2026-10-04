using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class LqaResidueExceptionTests
{
    [Theory]
    [InlineData("Thanks to @thecrimsonfucker for the idea.", "아이디어를 준 @thecrimsonfucker에게 고마워.")]   // a user handle
    [InlineData("战技-动作执行-新-Npc", "전기-동작 실행-신규-Npc")]                                          // kept from a Chinese source
    public void HandlesAndWordsKeptFromACjkSource_AreNotResidue(string source, string dest)
        => Assert.Null(LqaScanner.FindEnglishResidue(dest, source));

    [Theory]
    [InlineData("Hand Strap Attack", "Hand Strap 공격", "Hand")]
    [InlineData("新-Npc", "신규-Sword", "Sword")]
    public void OtherLatinWords_AreStillResidue(string source, string dest, string expected)
        => Assert.Equal(expected, LqaScanner.FindEnglishResidue(dest, source));
}
