using XTranslatorAi.Core.Text.ProjectContext;
using Xunit;

namespace XTranslatorAi.Tests;

public class ProjectContextTermEnforcerTests
{
    private static readonly ProjectContextTermInfo[] Terms =
    {
        new("Parry", 47, "패리"),
        new("Weapon Art", 295, "전기"),
        new("Deathblow", 59, null, new[] { "Deathblow => 치명적 일격" }),
    };

    [Fact]
    public void TermsWithATarget_AreCorrectedInEveryListFormat()
    {
        var context = "### 2. 주요 용어\n- Parry: 패링\n- Weapon Art => 무기 기술\n- Deathblow: 치명적 일격\n"
                      + "[주요 용어] - Parry: 패링 | - Weapon Art: 전기 |";

        var fixedContext = ProjectContextTermEnforcer.Apply(context, Terms);

        Assert.Equal("### 2. 주요 용어\n- Parry: 패리\n- Weapon Art => 전기\n- Deathblow: 치명적 일격\n"
                     + "[주요 용어] - Parry: 패리 | - Weapon Art: 전기 |", fixedContext);
    }

    [Fact]
    public void OtherLinesAndLongerNames_AreLeftAlone()
    {
        const string context = "- Elden Weapon Art: 엘든 전기\n- Parry / Perfect Parry: 패리 / 완벽한 패리\n패링 시 기 효과를 얻습니다.";

        Assert.Equal(context, ProjectContextTermEnforcer.Apply(context, Terms));
    }
}
