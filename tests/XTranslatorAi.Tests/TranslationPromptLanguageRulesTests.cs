using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class TranslationPromptLanguageRulesTests
{
    [Theory]
    [InlineData("batch")]
    [InlineData("text")]
    [InlineData("repair-batch")]
    [InlineData("repair-text")]
    public void KoreanGrammar_IsPresentWithoutPlaceholders_AndNotAppliedToOtherTargets(string path)
    {
        var korean = Build(path, "korean");
        var japanese = Build(path, "japanese");
        Assert.Contains("NPC는", korean);
        Assert.DoesNotContain("NPC는", japanese);
        Assert.Contains("matching meaning and grammatical use", korean);
        Assert.Contains("matching meaning and grammatical use", japanese);
    }

    [Theory]
    [InlineData("QUST:FULL", "quest title")]
    [InlineData("QUST:NNAM", "quest objective")]
    [InlineData("QUST:CNAM", "quest journal")]
    [InlineData("DIAL:FULL", "topic label")]
    [InlineData("INFO:NAM1", "spoken Korean")]
    public void FieldStyles_DistinguishLabelsAndNarrativeFromSpokenResponses(string rec, string expectedStyle)
    {
        var hint = TranslationStyleHints.Get("Hold the Line", rec.ToLowerInvariant());
        Assert.Contains(expectedStyle, hint);
        if (rec.EndsWith(":FULL")) Assert.DoesNotContain("spoken Korean", hint);
        if (rec == "QUST:CNAM") Assert.Contains("pending or completed", hint);
    }

    private static string Build(string path, string target) => path switch
    {
        "batch" => TranslationPrompt.BuildUserPrompt("english", target,
            [new(1, "NPCs cannot use this skill.", "PERK:DESC")], []),
        "text" => TranslationPrompt.BuildTextOnlyUserPrompt("english", target, "NPCs cannot use this skill.", []),
        "repair-batch" => TranslationPrompt.BuildRepairBatchUserPrompt("english", target,
            [new(1, "NPCs cannot use this skill.", "Incorrect translation", "PERK:DESC")], []),
        _ => TranslationPrompt.BuildRepairTextOnlyUserPrompt(new("english", target,
            "NPCs cannot use this skill.", "Incorrect translation", [])),
    };

    [Fact]
    public void ProbabilityExamples_AreOnlyAddedForKoreanProbabilityText_AndDoNotChangePayload()
    {
        var item = new TranslationItem(7, "Gain a __XT_PH_NUM_0000__ chance of a critical hit.", "PERK:DESC");
        var korean = TranslationPrompt.BuildUserPrompt("english", "korean", [item], []);
        Assert.Contains("Probability examples", korean);
        Assert.DoesNotContain("Probability examples", TranslationPrompt.BuildUserPrompt("english", "japanese", [item], []));
        Assert.DoesNotContain("Probability examples", TranslationPrompt.BuildUserPrompt("english", "korean", [new(7, "Restore Health.")], []));
        using var payload = System.Text.Json.JsonDocument.Parse(korean[(korean.IndexOf("Input JSON:", StringComparison.Ordinal) + "Input JSON:".Length)..]);
        Assert.Equal(item.Text, payload.RootElement.GetProperty("items")[0].GetProperty("text").GetString());
        Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
    }
}
