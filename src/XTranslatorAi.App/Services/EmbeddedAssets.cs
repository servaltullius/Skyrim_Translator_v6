using System;
using System.IO;
using System.Reflection;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public static class EmbeddedAssets
{
    public static string LoadMetaPrompt()
        => LoadMetaPrompt(BethesdaFranchise.ElderScrolls);

    public static string LoadMetaPrompt(BethesdaFranchise franchise)
        => LoadTextResource(
            franchise switch
            {
                BethesdaFranchise.Fallout => "XTranslatorAi.App.Assets.메타프롬프트_폴아웃.md",
                BethesdaFranchise.Starfield => "XTranslatorAi.App.Assets.메타프롬프트_스타필드.md",
                _ => "XTranslatorAi.App.Assets.메타프롬프트.md",
            }
        );

    public static string LoadDefaultGlossary()
        => LoadDefaultGlossary(BethesdaFranchise.ElderScrolls);

    public static string LoadDefaultGlossary(BethesdaFranchise franchise)
        => LoadTextResource(
            franchise switch
            {
                BethesdaFranchise.Fallout => "XTranslatorAi.App.Assets.기본용어집_폴아웃.md",
                BethesdaFranchise.Starfield => "XTranslatorAi.App.Assets.기본용어집_스타필드.md",
                _ => "XTranslatorAi.App.Assets.기본용어집.md",
            }
        );

    public static string? LoadBundledFranchiseTmSeed(BethesdaFranchise franchise)
        => franchise switch
        {
            BethesdaFranchise.ElderScrolls => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm.tsv"),
            BethesdaFranchise.Fallout => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.fallout4-franchise-tm.tsv"),
            BethesdaFranchise.Starfield => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.starfield-franchise-tm.tsv"),
            _ => null,
        };

    /// <summary>The rows of the current seed that changed since an earlier version (see BundledFranchiseTmSeedService).</summary>
    public static string? LoadBundledFranchiseTmSeedChanges(BethesdaFranchise franchise, string fromVersion)
        => (franchise, fromVersion) switch
        {
            (BethesdaFranchise.ElderScrolls, "v5") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v6-changes-from-v5.tsv"),
            (BethesdaFranchise.ElderScrolls, "v4") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v6-changes-from-v4.tsv"),
            (BethesdaFranchise.ElderScrolls, "v3") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v6-changes-from-v3.tsv"),
            (BethesdaFranchise.ElderScrolls, "v2") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v6-changes-from-v2.tsv"),
            (BethesdaFranchise.ElderScrolls, "v1") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v6-changes-from-v1.tsv"),
            _ => null,
        };

    /// <summary>The game translation dictionary built from the community Korean patch (see GameStringService).</summary>
    public static Stream? OpenBundledGameStrings()
        => Assembly.GetExecutingAssembly().GetManifestResourceStream("XTranslatorAi.App.Assets.GameStrings.game-strings-skyrim.gz");

    private static string LoadTextResource(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
