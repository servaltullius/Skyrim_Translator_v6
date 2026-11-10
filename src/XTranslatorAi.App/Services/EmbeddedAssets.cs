using System;
using System.IO;
using System.Reflection;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public static class EmbeddedAssets
{
    public static string LoadMetaPrompt()
        => LoadMetaPrompt(BethesdaFranchise.ElderScrolls);

    /// <summary>
    /// The meta prompt with CRLF line breaks, whatever the checkout it was built from wrote. The release is built from a
    /// checkout that writes CRLF and a working copy may hold LF; the prompt is part of every request's hash, so the
    /// requests of a local build and of the release differed, and the agent's request baseline failed in CI alone.
    /// </summary>
    public static string LoadMetaPrompt(BethesdaFranchise franchise)
        => LoadTextResource(
            franchise switch
            {
                BethesdaFranchise.Fallout => "XTranslatorAi.App.Assets.메타프롬프트_폴아웃.md",
                BethesdaFranchise.Starfield => "XTranslatorAi.App.Assets.메타프롬프트_스타필드.md",
                _ => "XTranslatorAi.App.Assets.메타프롬프트.md",
            }
        ).ReplaceLineEndings("\r\n");

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
            (BethesdaFranchise.ElderScrolls, "v27") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v27.tsv"),
            (BethesdaFranchise.ElderScrolls, "v26") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v26.tsv"),
            (BethesdaFranchise.ElderScrolls, "v25") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v25.tsv"),
            (BethesdaFranchise.ElderScrolls, "v24") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v24.tsv"),
            (BethesdaFranchise.ElderScrolls, "v23") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v23.tsv"),
            (BethesdaFranchise.ElderScrolls, "v22") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v22.tsv"),
            (BethesdaFranchise.ElderScrolls, "v21") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v21.tsv"),
            (BethesdaFranchise.ElderScrolls, "v20") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v20.tsv"),
            (BethesdaFranchise.ElderScrolls, "v19") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v19.tsv"),
            (BethesdaFranchise.ElderScrolls, "v18") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v18.tsv"),
            (BethesdaFranchise.ElderScrolls, "v17") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v17.tsv"),
            (BethesdaFranchise.ElderScrolls, "v16") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v16.tsv"),
            (BethesdaFranchise.ElderScrolls, "v15") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v15.tsv"),
            (BethesdaFranchise.ElderScrolls, "v14") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v14.tsv"),
            (BethesdaFranchise.ElderScrolls, "v13") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v13.tsv"),
            (BethesdaFranchise.ElderScrolls, "v12") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v12.tsv"),
            (BethesdaFranchise.ElderScrolls, "v11") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v11.tsv"),
            (BethesdaFranchise.ElderScrolls, "v10") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v10.tsv"),
            (BethesdaFranchise.ElderScrolls, "v9") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v9.tsv"),
            (BethesdaFranchise.ElderScrolls, "v8") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v8.tsv"),
            (BethesdaFranchise.ElderScrolls, "v7") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v7.tsv"),
            (BethesdaFranchise.ElderScrolls, "v6") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v6.tsv"),
            (BethesdaFranchise.ElderScrolls, "v5") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v5.tsv"),
            (BethesdaFranchise.ElderScrolls, "v4") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v4.tsv"),
            (BethesdaFranchise.ElderScrolls, "v3") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v3.tsv"),
            (BethesdaFranchise.ElderScrolls, "v2") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v2.tsv"),
            (BethesdaFranchise.ElderScrolls, "v1") => LoadTextResource("XTranslatorAi.App.Assets.TmSeeds.skyrim-tes-franchise-tm-v28-changes-from-v1.tsv"),
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
