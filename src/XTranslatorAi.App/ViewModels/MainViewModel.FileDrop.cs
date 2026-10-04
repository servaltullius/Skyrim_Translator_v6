using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// Files dropped on the window open the way the toolbar buttons do: a plugin (.esp/.esm/.esl) as a plugin project
/// and an xTranslator XML as an XML project. A plugin dropped on the previous-translation row of the project context
/// tab is linked as the earlier translation instead.
/// </summary>
public partial class MainViewModel
{
    private static readonly string[] PluginExtensions = { ".esp", ".esm", ".esl" };

    public static bool IsPluginFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && PluginExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsXmlFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".xml", StringComparison.OrdinalIgnoreCase);

    public bool CanOpenDroppedFile(string? path)
        => (IsPluginFile(path) || IsXmlFile(path)) && File.Exists(path) && CanOpenProject();

    public Task OpenDroppedFileAsync(string path)
    {
        if (!CanOpenDroppedFile(path))
        {
            return Task.CompletedTask;
        }

        return IsPluginFile(path) ? OpenPluginPathAsync(path) : OpenXmlPathAsync(path);
    }

    public bool CanLinkDroppedPreviousTranslation(string? path)
        => IsPluginFile(path) && File.Exists(path) && CanImportPreviousTranslation();

    public Task LinkDroppedPreviousTranslationAsync(string path)
        => CanLinkDroppedPreviousTranslation(path) ? ImportPreviousTranslationFromPathAsync(path) : Task.CompletedTask;
}
