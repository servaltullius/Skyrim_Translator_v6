using System;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class DescriptionStyleAndPayloadTests
{
    // War Ash descriptions mixed 합니다체 with "사용 가능." (48 of 50 rows) and read "80% of the effect" as an increase.
    [Theory]
    [InlineData("MGEF:DNAM")]
    [InlineData("SPEL:DESC")]
    [InlineData("ARMO:DESC")]
    public void EffectAndItemDescriptions_GetTheDescriptionStyle(string rec)
    {
        var hint = TranslationStyleHints.Get("Grab the weapon and leap. Usable on two-handed weapons.", rec);

        Assert.NotNull(hint);
        Assert.Contains("합니다체", hint);
        Assert.Contains("효과의 80%", hint);
    }

    [Theory]
    [InlineData("WEAP:FULL")]
    [InlineData("INFO:NAM1")]
    [InlineData("BOOK:DESC")]
    public void OtherRecords_DoNotGetTheDescriptionStyle(string rec)
        => Assert.DoesNotContain("합니다체", TranslationStyleHints.Get("Some text. More text.", rec) ?? "");

    // The default JSON encoder wrote Hangul and "<", ">" as escape sequences, several tokens per character.
    [Fact]
    public void BatchPayload_KeepsHangulAndTagsLiteral()
    {
        var prompt = TranslationPrompt.BuildUserPrompt("english", "korean",
            new[] { new TranslationItem(1, "Deals <mag> damage.", "MGEF:DNAM", Style: "합니다체로 쓰세요") },
            new[] { ("Whiterun", "화이트런") });

        Assert.Contains("\"text\":\"Deals <mag> damage.\"", prompt);
        Assert.Contains("합니다체로 쓰세요", prompt);
        Assert.Contains("화이트런", prompt);
        Assert.DoesNotContain(@"\u", prompt, StringComparison.Ordinal);
    }
}
