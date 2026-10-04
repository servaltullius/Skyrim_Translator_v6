using XTranslatorAi.Core.Translation;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// The official translation writes an enchanted item as "base - enchantment" ("제국군 전투화 - 중급 냉기 저항"); mods that
/// reuse vanilla enchantments on their own items ("Vagrant Robes of Major Conjuring", 3,984 names in 21 local mods)
/// were translated as phrases ("감소의 냉기 제국군 부츠").
/// </summary>
public class EnchantmentNameIndexTests
{
    private static readonly (string, string)[] Memory =
    {
        ("Robes of Major Conjuring", "로브 - 중급 소환마법"),
        ("Hooded Robes of Major Conjuring", "두건 달린 로브 - 중급 소환마법"),
        ("Imperial Boots of Dwindling Frost", "제국군 전투화 - 중급 냉기 저항"),
        ("Elven Boots of Dwindling Frost", "엘프 전투화 - 중급 냉기 저항"),
        ("Iron Sword of Frost", "철 검 - 하급 냉기"),
        ("Steel Sword of Frost", "강철 검 - 하급 냉기"),
        ("Glass Bow of Frost", "유리 활 - 냉기"),
        ("Orcish Mace of Frost", "오크 철퇴 - 냉기"),
        ("Iron Sword of Embers", "철 검 - 불씨"),
        ("Shield of Ysgramor", "이스그라모르의 방패"),
    };

    [Fact]
    public void KnownSuffix_GivesTheOfficialForm_OnlyForItemNames()
    {
        var index = EnchantmentNameIndex.Build(Memory);

        var hint = index.GetHint("Vagrant Robes of Major Conjuring", "ARMO:FULL");
        Assert.NotNull(hint);
        Assert.Contains("- 중급 소환마법", hint);
        Assert.Contains("\"Vagrant Robes\"", hint);
        Assert.Contains("- 중급 냉기 저항", index.GetHint("Vagrant Boots of Dwindling Frost", "ARMO:FULL"));

        Assert.Null(index.GetHint("Vagrant Robes of Major Conjuring", "BOOK:FULL"));
        Assert.Null(index.GetHint("Robes of the Night Mother", "ARMO:FULL"));
    }

    [Fact]
    public void SuffixTheOfficialTranslationWritesDifferently_OrOnlyOnce_IsNotUsed()
    {
        var index = EnchantmentNameIndex.Build(Memory);

        // "of Frost" is 하급 냉기 twice and 냉기 twice; "of Embers" occurs once.
        Assert.Null(index.GetHint("Vagrant Sword of Frost", "WEAP:FULL"));
        Assert.Null(index.GetHint("Vagrant Sword of Embers", "WEAP:FULL"));
        Assert.Equal(2, index.Count);
    }

    /// <summary>An ending the official translation mostly writes another way ("X의 Y") is not an enchantment form.</summary>
    [Fact]
    public void SuffixMostlyTranslatedOtherwise_IsNotUsed()
    {
        var memory = new[]
        {
            ("Shield of Kings", "방패 - 왕"), ("Helm of Kings", "투구 - 왕"),
            ("Crown of Kings", "왕들의 왕관"), ("Sword of Kings", "왕들의 검"), ("Hall of Kings", "왕들의 전당"),
        };

        Assert.Null(EnchantmentNameIndex.Build(memory).GetHint("Vagrant Boots of Kings", "ARMO:FULL"));
    }

    [Fact]
    public async Task ItemNameWithAKnownEnchantment_SendsTheHintWithTheRow()
    {
        await using var fixture = await TranslationRunFixture.CreateAsync(
            ("Vagrant Robes of Major Conjuring", "ARMO:FULL"), ("Vagrant Robes", "ARMO:FULL"));

        await fixture.Service.TranslateIdsAsync(fixture.Request with
        {
            ReferenceNameMemory = Memory, BatchSize = 1, TargetLang = "korean", UseRecStyleHints = true,
        });

        var prompts = fixture.Client.Requests.Select(r => r.Contents[0].Parts[0].Text!).ToList();
        Assert.Contains(prompts, p => p.Contains("Vagrant Robes of Major Conjuring", StringComparison.Ordinal)
            && p.Contains("- 중급 소환마법", StringComparison.Ordinal));
        Assert.DoesNotContain(prompts, p => !p.Contains("of Major Conjuring", StringComparison.Ordinal)
            && p.Contains("- 중급 소환마법", StringComparison.Ordinal));
    }
}
