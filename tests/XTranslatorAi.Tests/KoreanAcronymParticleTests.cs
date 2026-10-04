using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// An all-caps acronym is read letter by letter, so its particle is certain: AP를 (에이피), MCM을 (엠시엠).
/// The quality check flagged "AP을" in 9 reviewed War Ash Pack 1 rows but nothing fixed it. The 2026-10-04 audit
/// left every acronym to the check because a short all-caps word may be read as a word; only acronyms that are
/// surely spelled out are rewritten.
/// </summary>
public class KoreanAcronymParticleTests
{
    [Theory]
    [InlineData("[질주 버튼]을 길게 누르면 AP을 소모하여 주문 효과를 강화합니다.", "[질주 버튼]을 길게 누르면 AP를 소모하여 주문 효과를 강화합니다.")]
    [InlineData("MCM를 열고 설정하세요.", "MCM을 열고 설정하세요.")]
    [InlineData("NPC은 공격하지 않습니다.", "NPC는 공격하지 않습니다.")]
    [InlineData("HP과 매지카를 회복합니다.", "HP와 매지카를 회복합니다.")]
    // The 2026-10-04 audit case, kept unchanged until now.
    [InlineData("NPC을 고용할 수 있다.", "NPC를 고용할 수 있다.")]
    [InlineData("DLC가 필요합니다. NPC이 말합니다.", "DLC가 필요합니다. NPC가 말합니다.")]
    public void Fix_ChoosesTheParticleByTheAcronymReading(string text, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", text));

    [Theory]
    [InlineData("Rune Stone을 사용합니다.")]
    [InlineData("Nexus를 확인하세요.")]
    [InlineData("셉팀 VII가 다스렸다.")]
    [InlineData("WARNING이 표시됩니다.")]
    [InlineData("AP를 소모합니다.")]
    [InlineData("총AP을 확인합니다.")]
    [InlineData("AP이다.")]
    // Short all-caps words with vowels may be read as words.
    [InlineData("STOP을 누르세요.")]
    [InlineData("PAK을 압축합니다.")]
    public void Fix_LeavesWordsAndCorrectParticlesAlone(string text)
        => Assert.Equal(text, KoreanTranslationFixer.Fix("korean", text));
}
