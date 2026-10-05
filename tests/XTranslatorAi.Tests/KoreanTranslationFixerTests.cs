using XTranslatorAi.Core.Text;
using Xunit;

namespace XTranslatorAi.Tests;

public class KoreanTranslationFixerTests
{
    [Fact]
    public void Fix_ParticleStepExtraction_KeepsAllParticleCasesPassing()
    {
        var cases = new (string Input, string Expected)[]
        {
            ("체력 을(를) 흡수합니다.", "체력을 흡수합니다."),
            ("Aela은(는) 동료입니다.", "Aela는 동료입니다."),
            ("매지카을 흡수합니다.", "매지카를 흡수합니다."),
            ("블러드 을 흡수합니다.", "블러드를 흡수합니다."),
            ("방패은 부서집니다.", "방패는 부서집니다."),
            ("아니요. 저는은 솔리튜드에서 삽니다.", "아니요. 저는 솔리튜드에서 삽니다."),
        };

        foreach (var item in cases)
        {
            var output = KoreanTranslationFixer.Fix("korean", item.Input);
            Assert.Equal(item.Expected, output);
        }
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle()
    {
        var input = "체력 을(를) 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력을 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle_ForVowelEndingNoun()
    {
        var input = "매지카을(를) 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle_ParenFirstStyle()
    {
        var input = "매지카(을)를 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle()
    {
        var input = "체력은(는) 회복됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력은 회복됩니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle_ForVowelEndingNoun()
    {
        var input = "매지카 은(는) 회복됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카는 회복됩니다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle_ParenFirstStyle()
    {
        var input = "매지카(은)는 회복됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카는 회복됩니다.", output);
    }

    [Fact]
    public void Fix_ChoosesSubjectParticle_ForParenthesizedParticle_ParenFirstStyle()
    {
        var input = "체력(이)가 감소합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력이 감소합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenWrong()
    {
        var input = "매지카을 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsSeparatedObjectParticle_WhenWrong()
    {
        var input = "블러드 을 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("블러드를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ChoosesObjectParticle_ForParenthesizedParticle_ForLatinNoun()
    {
        var input = "Aela을(를) 만났다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("Aela를 만났다.", output);
    }

    [Fact]
    public void Fix_ChoosesTopicParticle_ForParenthesizedParticle_ForLatinNoun()
    {
        var input = "Aela은(는) 동료입니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("Aela는 동료입니다.", output);
    }

    // An acronym is read letter by letter, and only L, M, N and R (엘, 엠, 엔, 알) end in a consonant.
    // Other Latin words keep the last-letter choice: a marker must become one form or the other.
    [Theory]
    [InlineData("NPC을(를) 고용합니다.", "NPC를 고용합니다.")]
    [InlineData("DLC를(을) 설치하세요.", "DLC를 설치하세요.")]
    [InlineData("NPC은(는) 공격하지 않습니다.", "NPC는 공격하지 않습니다.")]
    [InlineData("MCM를(을) 엽니다.", "MCM을 엽니다.")]
    [InlineData("HTML는(은) 지원하지 않습니다.", "HTML은 지원하지 않습니다.")]
    [InlineData("Skyrim을(를) 탐험합니다.", "Skyrim을 탐험합니다.")]
    public void Fix_ResolvesMarkerAfterLatinWord_ByPronunciation(string input, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", input));

    // The 1.12 regression run kept "[엘든 패리]을(를)": a closing bracket or quote between the word and the marker hid it.
    [Theory]
    [InlineData("[엘든 패리]을(를) 사용할 수 있습니다.", "[엘든 패리]를 사용할 수 있습니다.")]
    [InlineData("\"검의 의지\"을(를) 얻었다.", "\"검의 의지\"를 얻었다.")]
    [InlineData("[Hand Strap]을(를) 만든다.", "[Hand Strap]을 만든다.")]
    public void Fix_ResolvesMarkerAfterAClosingBracketOrQuote(string input, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", input));

    // "LEVEL UP" is read as words (레벨 업을), so UP is no acronym read letter by letter (유피를); and 몇일까 is 몇 + 일까
    // ("how many would it be?"), not the misspelled 몇일 (며칠).
    [Theory]
    [InlineData("LEVEL UP을 축하합니다.", "LEVEL UP을 축하합니다.")]
    [InlineData("남은 건 몇일까?", "남은 건 몇일까?")]
    [InlineData("몇일 동안 기다렸다.", "며칠 동안 기다렸다.")]
    [InlineData("AP을 소모한다.", "AP를 소모한다.")]
    public void Fix_LeavesWordsReadAsWordsAndTheCopula(string input, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", input));

    // 들를 is 들르다 ("stop by"), not 들 + 를: the fixer turned "여관에 들를 거야" into 들을 (hear) and the quality check
    // reported the correct form.
    [Theory]
    [InlineData("여관에 들를 거야.")]
    [InlineData("잠시 들를 수 있다.")]
    public void Fix_KeepsTheVerbDeulreuda(string text)
    {
        Assert.Equal(text, KoreanTranslationFixer.Fix("korean", text));
        Assert.Null(LqaHeuristics.FindHangulParticleMismatchSuggestion(text));
    }

    // Parentheses qualify the word before them, so the marker follows that word (32eb1d7 picked 양손).
    [Theory]
    [InlineData("도끼(양손)을(를) 든다.", "도끼(양손)를 든다.")]
    [InlineData("물약(대)이(가) 있다.", "물약(대)이 있다.")]
    public void Fix_ResolvesMarkerAfterParentheses_ByTheWordBeforeThem(string input, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", input));

    [Theory]
    [InlineData("NPC 를 고용합니다.", "NPC를 고용합니다.")]
    [InlineData("Rune 을 새겼다.", "Rune을 새겼다.")]
    [InlineData("Nexus 를 방문하세요.", "Nexus를 방문하세요.")]
    [InlineData("NPC 는 공격하지 않습니다.", "NPC는 공격하지 않습니다.")]
    public void Fix_JoinsSpacedParticleAfterLatinWord_WithoutRewritingIt(string input, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", input));

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenFollowedByPunctuation()
    {
        var input = "매지카을… 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를… 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenSeparatedByZeroWidthSpace()
    {
        var input = "매지카\u200B을 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_ForConsonantEndingNoun_WhenWrong()
    {
        var input = "검를 듭니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("검을 듭니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedObjectParticle_WhenFollowedByEmDash()
    {
        var input = "검를— 듭니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("검을— 듭니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedTopicParticle_WhenWrong()
    {
        var input = "방패은 부서집니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("방패는 부서집니다.", output);
    }

    [Fact]
    public void Fix_ReplacesStatDativeParticle()
    {
        var input = "체력에게 <mag>포인트의 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("체력에 <mag>포인트의 피해를 입힙니다.", output);
    }

    [Fact]
    public void Fix_ReplacesStatDativeParticle_WithSuffix()
    {
        var input = "매지카에게는 그 절반의 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("ko", input);
        Assert.Contains("매지카에는", output);
        Assert.DoesNotContain("매지카에게는", output);
    }

    [Fact]
    public void Fix_ReplacesStatDativeParticle_FromForm()
    {
        var input = "지구력에게서 <mag>포인트를 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("지구력에서 <mag>포인트를 흡수합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsSeparatedSubjectParticle_BasedOnBatchim()
    {
        var input = "중갑 가 <mag>포인트 강화됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Contains("중갑이", output);
        Assert.DoesNotContain("중갑 가", output);
    }

    [Theory]
    [InlineData("체력 가 <mag>포인트 회복됩니다.", "체력이 <mag>포인트 회복됩니다.")]
    [InlineData("매지카 이 <dur>초 동안 재생되지 않습니다.", "매지카가 <dur>초 동안 재생되지 않습니다.")]
    [InlineData("회복마법 가 <25%> 강화됩니다.", "회복마법이 <25%> 강화됩니다.")]
    [InlineData("한손무기 가 +<mag> 증가합니다.", "한손무기가 +<mag> 증가합니다.")]
    [InlineData("지구력 가 15 증가합니다.", "지구력이 15 증가합니다.")]
    public void Fix_JoinsSeparatedSubjectParticle_AfterStatOrSkillBeforeNumber(string input, string expected)
        => Assert.Equal(expected, KoreanTranslationFixer.Fix("korean", input));

    [Fact]
    public void Fix_ReordersMisplacedDurationTokenAfterTimePhrase()
    {
        var input = "늑대인간초 동안 <150>초 의 형상을 취합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("<150>초 동안 늑대인간의 형상을 취합니다.", output);
    }

    [Theory]
    [InlineData("모든 적은 3초 동안 <25>포인트의 피해를 입습니다.")]
    [InlineData("모든 적은 3 초 동안 <25>포인트의 피해를 입습니다.")]
    [InlineData("적은 10초 동안 <5>의 냉기 피해를 입습니다.")]
    // Count words are durations as well: "수 초", "몇 초", "매 초" moved the token into the subject (적은 수포인트).
    [InlineData("적은 수 초 동안 <10>포인트의 피해를 입습니다.")]
    [InlineData("대상은 몇 초 동안 <25>의 피해를 입습니다.")]
    [InlineData("매 초 동안 <5>포인트씩 회복합니다.")]
    [InlineData("적은 수초 동안 <10>포인트의 피해를 입습니다.")]
    public void Fix_KeepsCorrectLiteralDurationBeforeValueToken(string input)
    {
        // "N초 동안" with a literal number is already a correct duration; the token after it is the magnitude.
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Fix_DurationAndArtifactExtraction_KeepsComplexStringsStable()
    {
        var cases = new (string Input, string Expected)[]
        {
            ("피해를 입으면 <25%> <5>초 동안 확률로 투명화하기 상태가 됩니다.", "피해를 입으면 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다."),
            ("피해를 입을 시 <25%> 확률로 <5>초 동안 투명화하기 합니다.", "피해를 입을 시 <25%> 확률로 <5>초 동안 투명화합니다."),
            ("습격이다! 무기를 물건 전달!", "습격이다! 무기를 내려!"),
            ("<15>포인트 체력포인트를 흡수하고 <7><3>초 동안 포인트초포인트의 출혈 피해를 입힙니다.", "<15>포인트 체력을 흡수하고 <3>초 동안 <7>포인트의 출혈 피해를 입힙니다."),
            ("늑대인간초 동안 <150>초 의 형상을 취합니다.", "<150>초 동안 늑대인간의 형상을 취합니다."),
        };

        foreach (var item in cases)
        {
            var output = KoreanTranslationFixer.Fix("korean", item.Input);
            Assert.Equal(item.Expected, output);
        }
    }

    [Fact]
    public void Fix_CleansUpNumericPlaceholderUnitGarbage_ForBleedingAbsorbString()
    {
        var input = "<15>포인트 체력포인트를 흡수하고 <7><3>초 동안 포인트초포인트의 출혈 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("<15>포인트 체력을 흡수하고 <3>초 동안 <7>포인트의 출혈 피해를 입힙니다.", output);
    }

    [Fact]
    public void Fix_DoesNotRewriteAttributiveNeun_AsTopicParticle()
    {
        var input = "달이 떠있는 동안 <mag> 의 피해를 입힙니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Contains("떠있는 동안", output);
        Assert.DoesNotContain("떠있은", output);
    }

    [Fact]
    public void Fix_CollapsesDuplicateEffectWord()
    {
        Assert.Equal("이 효과 효과적으로 막아냅니다.", KoreanTranslationFixer.Fix("korean", "이 효과 효과적으로 막아냅니다."));
        var input = "치명적인 마법부여 효과 효과가 적을 비틀거리게 합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("치명적인 마법부여 효과가 적을 비틀거리게 합니다.", output);
    }

    [Fact]
    public void Fix_RemovesRedundantStatPointsNoun_ForStaminaAbsorbString()
    {
        var input = "일정 확률로 적을 비틀거리게 만들며 <15>포인트 지구력포인트를 흡수합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("일정 확률로 적을 비틀거리게 만들며 <15>포인트 지구력을 흡수합니다.", output);
    }

    [Fact]
    public void Fix_ReordersProbabilityMarker_WhenItAppearsAfterDuration()
    {
        var input = "피해를 입으면 <25%> <5>초 동안 확률로 투명화 상태가 됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("피해를 입으면 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다.", output);
    }

    [Fact]
    public void Fix_CleansUpHaGiInfinitive_BeforeStateNoun()
    {
        var input = "피해를 입었을 때 <25%> 확률로 <5>초 동안 투명화하기 상태가 됩니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("피해를 입었을 때 <25%> 확률로 <5>초 동안 투명화 상태가 됩니다.", output);
    }

    [Fact]
    public void Fix_CleansUpHaGiInfinitive_BeforeHapNidaVerb()
    {
        var input = "피해를 입을 시 <25%> 확률로 <5>초 동안 투명화하기 합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("피해를 입을 시 <25%> 확률로 <5>초 동안 투명화합니다.", output);
    }

    [Fact]
    public void Fix_RewritesDropYourWeaponsArtifact()
    {
        var input = "습격이다! 무기를 물건 전달!";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("습격이다! 무기를 내려!", output);
    }

    [Fact]
    public void Fix_CollapsesDuplicateTopicParticle_AfterPronoun()
    {
        var input = "아니요. 저는은 솔리튜드에서 삽니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("아니요. 저는 솔리튜드에서 삽니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedSubjectParticle_ConsonantEndingNoun()
    {
        // 력 has 받침 → should be 이, not 가
        var input = "지구력가 부족합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("지구력이 부족합니다.", output);
    }

    [Fact]
    public void Fix_CorrectsAttachedSubjectParticle_VowelEndingNoun()
    {
        // 카 has no 받침 → should be 가, not 이
        var input = "매지카이 부족합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("매지카가 부족합니다.", output);
    }

    [Fact]
    public void Fix_LeavesSeparatedSubjectParticle_OutsideStatPhrases()
    {
        // A lone "가"/"이" is too often the verb or the demonstrative ("그만 가 봐", "그대가 이 책").
        // Only the stat phrase before a placeholder ("중갑 가 <mag>") is joined.
        var input = "중갑 가 무겁다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("중갑 가 무겁다.", output);
    }

    [Fact]
    public void Fix_DoesNotChangeCorrectSubjectParticle()
    {
        var input = "지구력이 부족합니다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("지구력이 부족합니다.", output);
    }

    [Fact]
    public void Fix_DoesNotChangeSubjectParticle_OnSingleSyllableNoun()
    {
        // 1-syllable guard: "집가" could be verb 가다, skip
        var input = "집가 멀다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("집가 멀다.", output);
    }

    [Fact]
    public void Fix_DoesNothing_ForNonKorean()
    {
        var input = "체력에게 <mag> points";
        var output = KoreanTranslationFixer.Fix("english", input);
        Assert.Equal(input, output);
    }

    // --- SpellingFixStep tests ---

    // "되었/되어" are correct, not spelling errors: the official translation writes "되었습니다" 42 times to "됐습니다"
    // 3, and the fixer also runs on TM hits, so contracting them rewrote official text (decision 4, 2026-10-05).
    [Theory]
    [InlineData("이것은 사용되어 왔다.")]
    [InlineData("마법이 해제되었습니다.")]
    [InlineData("전쟁이 시작되었다.")]
    [InlineData("준비가 됐어.")]
    public void Fix_KeepsFullAndContractedFormsOfDoeda(string input)
        => Assert.Equal(input, KoreanTranslationFixer.Fix("korean", input));

    [Fact]
    public void Fix_Corrects몇일_To_며칠()
    {
        var input = "몇일 후에 돌아오겠다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("며칠 후에 돌아오겠다.", output);
    }

    [Fact]
    public void Fix_Corrects됬_To_됐()
    {
        var input = "마을이 파괴됬다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("마을이 파괴됐다.", output);
    }

    [Fact]
    public void Fix_DoesNotChange_AlreadyCorrect_돼()
    {
        var input = "이렇게 하면 안 돼.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("이렇게 하면 안 돼.", output);
    }

    [Theory]
    [InlineData("해치지 않되, 놓아주지도 마라.")]
    [InlineData("값을 깎아 주지는 않되 덤은 주겠다.")]
    public void Fix_KeepsConnective_지않되(string input)
    {
        // "-지 않되"는 맞는 연결 어미다. 예전 규칙은 이를 맞춤법에도 없는 "않돼"로 바꿨다.
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Fix_DoesNotChange_되다_Standalone()
    {
        // 되 + consonant (되다, 되면, 되니) is correct and should not be changed
        var input = "이것은 사용되면 좋다.";
        var output = KoreanTranslationFixer.Fix("korean", input);
        Assert.Equal("이것은 사용되면 좋다.", output);
    }
}
