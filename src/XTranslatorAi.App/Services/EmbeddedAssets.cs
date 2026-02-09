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
