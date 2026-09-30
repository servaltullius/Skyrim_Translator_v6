using System.Text.Json;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public class TranslationPromptGlossaryPayloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonPrompts_ContainActualGlossaryTerms(bool repair)
    {
        var pairs = new[] { (Source: "Sun's Dawn", Target: "2월") };
        var prompt = repair
            ? TranslationPrompt.BuildRepairBatchUserPrompt("english", "korean", Array.Empty<RepairTranslationItem>(), pairs)
            : TranslationPrompt.BuildUserPrompt("english", "korean", Array.Empty<TranslationItem>(), pairs);
        using var json = JsonDocument.Parse(prompt[prompt.IndexOf("{\"source_language\"", StringComparison.Ordinal)..]);
        var glossary = json.RootElement.GetProperty("glossary");
        Assert.Equal("Sun's Dawn", glossary[0].GetProperty("source").GetString());
        Assert.Equal("2월", glossary[0].GetProperty("target").GetString());
    }
}
