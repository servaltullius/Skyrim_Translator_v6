using System.Xml.Linq;

namespace XTranslatorAi.Tests;

/// <summary>Smaller UI defects from the 2026-10-04 audit.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Theory]
    [InlineData("ProjectGlossaryTabView.xaml", "GlossarySourceTerm")]
    [InlineData("ProjectGlossaryTabView.xaml", "GlossaryTargetTerm")]
    [InlineData("GlobalGlossaryTabView.xaml", "GlobalGlossarySourceTerm")]
    [InlineData("GlobalGlossaryTabView.xaml", "GlobalGlossaryTargetTerm")]
    [InlineData("GlobalTranslationMemoryTabView.xaml", "FranchiseTranslationMemorySourceText")]
    [InlineData("GlobalTranslationMemoryTabView.xaml", "FranchiseTranslationMemoryDestText")]
    public void AddButtons_AreEnabledWhileTyping_NotOnlyAfterTheTextBoxLosesFocus(string view, string property)
    {
        // "추가" is enabled from these two texts; with the default LostFocus trigger it stayed disabled while typing.
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var box = Assert.Single(XDocument.Load(MainWindowSourcePath(Path.Combine("Views", view))).Root!.Descendants(wpf + "TextBox"),
            element => ((string?)element.Attribute("Text"))?.StartsWith("{Binding " + property + ",", StringComparison.Ordinal) == true
                       || (string?)element.Attribute("Text") == "{Binding " + property + "}");
        Assert.Equal("{Binding " + property + ", UpdateSourceTrigger=PropertyChanged}", (string?)box.Attribute("Text"));
    }
}
