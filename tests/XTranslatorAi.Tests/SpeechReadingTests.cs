using XTranslatorAi.Core.Text.Lqa.Internal;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;

namespace XTranslatorAi.Tests;

/// <summary>
/// The tone checks read a line's speech one way (SpeechReading). The dialogue check left out "(고개를 끄덕인다)" but not
/// "*웃음*", the speaker check the other way round, and neither read "…요~". Over 83,033 dialogue lines (the Korean
/// patch's 47,963 and the user's 56 projects'), some 240 read otherwise once they were brought together; of the patch's
/// 40,787 NPC lines, 4 for the speaker check.
/// </summary>
public sealed class SpeechReadingTests
{
    [Theory]
    // A stage direction of any kind is left out, before or after the words.
    [InlineData("알겠소. (고개를 끄덕인다)", "알겠소.")]
    [InlineData("알았어. *웃음*", "알았어.")]
    [InlineData("[한숨을 쉰다] 그래, 가자.", "그래, 가자.")]
    // A quotation is someone else's words.
    [InlineData("그가 \"살려 주세요!\" 하고 울부짖더군.", "그가 하고 울부짖더군.")]
    // A line of nothing but a quotation is judged by it, a "(을/를)" inside it or not (a bard's tale).
    [InlineData("'홀기어(을/를) 만났다네.'", "'홀기어(을/를) 만났다네.'")]
    // A quotation with a stage direction beside it is read aloud, or said in another's voice: no words of its own.
    [InlineData("(낭독하며) \"내 앞엔 새로운 삶이 펼쳐졌다.\"", "")]
    [InlineData("\"난 인간을 그만두겠다, 세라나!\" *큭큭 웃는다*", "")]
    [InlineData("(고개를 끄덕인다)", "")]
    // A heading drawn with dashes is no speech.
    [InlineData("----펠 황야----", "")]
    public void OwnWords(string line, string own)
        => Assert.Equal(own, string.Join(" ", SpeechReading.OwnWords(line).Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Theory]
    // A drawn-out ending is read: it was left with no level.
    [InlineData("정말이에요~", "Haeyo")]
    [InlineData("뭐라구요--", "Haeyo")]
    [InlineData("알았어~", "Unknown")]
    // A clause left open, or the particle 까지, is not 반말's 지 (a trailing dash let it be read as one).
    [InlineData("제가 잘 할 수 있을지--", "Unknown")]
    [InlineData("어찌 감사를 해야 할지.", "Unknown")]
    [InlineData("리버우드까지.", "Unknown")]
    // A heading of a hold's name.
    [InlineData("----리치 영지----", "Unknown")]
    [InlineData("----펠 황야----", "Unknown")]
    [InlineData("그건 내가 할 일이지.", "Casual")]
    public void TheClassifier_ReadsTheEndsAlike(string text, string expected)
        => Assert.Equal(expected, LqaToneClassifier.Classify(text).ToString());

    /// <summary>The speaker check reads a line with a stage direction after it by its words, not by the direction's verb.</summary>
    [Fact]
    public void TheSpeakerCheck_ReadsTheWordsBesideAStageDirection()
    {
        Assert.True(SpeakerToneConsistencyRule.SameLevel("알겠소. (고개를 끄덕인다)", "좋소."));
        Assert.True(SpeakerToneConsistencyRule.SameLevel("이게 뭔지 알겠나? (특이한 보석을 보여준다)", "자네도 알겠나?"));
        Assert.False(SpeakerToneConsistencyRule.SameLevel("(고개를 끄덕인다)", "(고개를 끄덕인다)"));
    }

    /// <summary>The dialogue check reads 반말 before a stage direction between asterisks, as it does before one in brackets.</summary>
    [Fact]
    public void TheDialogueCheck_ReadsTheWordsBeforeAStageDirectionInAsterisks()
    {
        Assert.True(DialogueToneConsistencyRule.SameSpeech("그럼 됐어. *미소 짓는다*", "그럼 됐어. (미소 짓는다)"));
        Assert.False(DialogueToneConsistencyRule.SameSpeech("(낭독하며) \"새로운 삶이 펼쳐졌다.\"", "(낭독하며) \"새로운 삶이 펼쳐졌다.\""));
    }

    /// <summary>A last sentence left open trails the one before it: the line is 반말 by "네 몫이야", not unread by "할지".</summary>
    [Fact]
    public void TheDialogueCheck_ReadsTheSentenceBeforeOneLeftOpen()
    {
        Assert.True(DialogueToneConsistencyRule.SameSpeech("그건 네 몫이야. 그걸로 뭘 할지.", "그건 내 일이야."));
        Assert.True(DialogueToneConsistencyRule.SameSpeech("그건 당신 몫이에요. 그걸로 뭘 할지.", "그건 제 일이에요."));
    }

    /// <summary>합쇼체 is told from 해요체 on the speaker's own words, a drawn-out end or a stage direction after them left out.</summary>
    [Fact]
    public void ThePatchLevel_CountsFormalLinesAsTheLevelReadsThem()
    {
        // Read by their last marks, three of these four were 해요체's, and the speaker was.
        var lines = new[] { "감사합니다~", "알겠습니다. (고개를 숙인다)", "환영합니다!", "무엇을 찾으십니까~", "네, 알겠어요.", "고마워요.", "또 오세요.", "그래요." };
        Assert.Equal("합쇼체", SpeakerToneConsistencyRule.PatchLevel(lines));
    }
}
