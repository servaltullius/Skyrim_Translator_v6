using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

/// <summary>
/// The level a base game NPC speaks in the Korean patch, which the requests name when none is decided: in the Teldryn
/// comparison the GUI wrote him in 반말 and the agent mode's Gemini Flash in 해요체, where the patch has 하오체.
/// </summary>
public sealed class KoreanPatchLevelTests
{
    [Fact]
    public void HaoSpeaker_MixingJiAndGunLines_IsHao()
    {
        // Teldryn Sero's lines in the Korean patch: 하오체 with ~지, ~군 between.
        var lines = new[]
        {
            "당신이 날 고용할 생각이오?", "좋소, 같이 가겠소.", "레이븐 록은 언제나 이렇지.", "재미있겠군.", "조심하시오.",
            "그건 내가 맡겠소.", "드로거는 질색이야.", "여기서 오래 머물 생각은 없소.", "칼을 뽑을 때가 됐군.", "다음엔 어디로 가오?",
        };

        Assert.Equal("하오체", DialogueSpeakerNames.KoreanPatchLevel(lines));
    }

    [Fact]
    public void CasualSpeaker_WithAStrayHaoLine_IsCasual()
    {
        var lines = new[]
        {
            "같이 가자.", "그건 내가 할게.", "조심해.", "여기 좀 이상해.", "고마워, 정말로.", "어디로 가는 거야?",
            "난 괜찮아.", "다음엔 내가 앞장설게.", "이건 좀 무섭네.", "무슨 생각을 하는 거요?",
        };

        Assert.Equal("반말", DialogueSpeakerNames.KoreanPatchLevel(lines));
    }

    [Theory]
    [InlineData("합쇼체", new[] { "환영합니다.", "대사관에 오신 것을 환영합니다.", "무엇을 도와드릴까요?", "그건 곤란합니다.", "알겠습니다.",
        "조사는 계속됩니다.", "이쪽으로 오십시오.", "감사합니다.", "기다리십시오.", "물론입니다." })]
    [InlineData("해요체", new[] { "어서 오세요.", "뭘 도와드릴까요?", "그건 안 돼요.", "알겠어요.", "조심하세요.", "고마워요.",
        "여기 있어요.", "잠깐만요, 금방 올게요.", "정말 그래요?", "좋아요, 가요." })]
    public void PoliteSpeaker_IsFormalOrPolite_ByItsEndings(string expected, string[] lines)
        => Assert.Equal(expected, DialogueSpeakerNames.KoreanPatchLevel(lines));

    [Fact]
    public void FewLines_AreNoLevel()
        => Assert.Null(DialogueSpeakerNames.KoreanPatchLevel(new[] { "좋소.", "가겠소.", "조심하시오." }));
}
