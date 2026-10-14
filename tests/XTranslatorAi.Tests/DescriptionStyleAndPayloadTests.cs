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

    // Elden Rim puts bare item names in ARMO:DESC and SPEL:DESC; under the 합니다체 hint "Evoker Crossbow" came back as
    // "기원사 쇠뇌입니다." and "Deathblow" as "결정타를 가합니다." (1.12 eval E0135, E0199, E0205, E0211, E0215).
    [Theory]
    [InlineData("Evoker-Pure Ebony - Sword and Scabbard", "ARMO:DESC")]
    [InlineData("Evoker Sword - L", "ARMO:DESC")]
    [InlineData("Evoker Crossbow", "ARMO:DESC")]
    [InlineData("Evoker-Pure Ebony - Scabbard", "ARMO:DESC")]
    [InlineData("Deathblow", "SPEL:DESC")]
    [InlineData("Ring of Rim", "MGEF:DNAM")]
    public void ShortTitleCaseLabels_GetANounLabelHint(string source, string rec)
    {
        var hint = TranslationStyleHints.Get(source, rec);

        Assert.NotNull(hint);
        Assert.DoesNotContain("합니다체", hint);
        Assert.Contains("noun phrase", hint);
    }

    // Sentence-like descriptions without a final period keep the 합니다체 hint (same eval, E0129, E0133, E0143).
    [Theory]
    [InlineData("Release the loaded spell", "SPEL:DESC")]
    [InlineData("Press and hold the sprint key to trigger the script", "SPEL:DESC")]
    [InlineData("Targets with this buff cannot blocking", "SPEL:DESC")]
    [InlineData("You move more quietly.", "MGEF:DNAM")]
    [InlineData("Summons a Dremora Lord for <dur> Seconds", "MGEF:DNAM")]
    [InlineData("Fortify Health By 50%", "MGEF:DNAM")]
    [InlineData("Shield of the Divine Crusader, gift of Julianos.", "ARMO:DESC")]
    public void SentenceDescriptions_KeepTheDescriptionStyle(string source, string rec)
        => Assert.Contains("합니다체", TranslationStyleHints.Get(source, rec));

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
