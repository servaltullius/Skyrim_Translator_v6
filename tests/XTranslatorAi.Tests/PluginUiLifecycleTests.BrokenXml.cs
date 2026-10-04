namespace XTranslatorAi.Tests;

/// <summary>
/// A truncated or hand-edited XML failed with the parser's English text ("Unexpected end of file while parsing Name
/// has occurred. Line 8, position 19.") and no hint of what to do.
/// </summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task OpeningABrokenXml_SaysInKoreanWhereItBreaks()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var xml = Path.Combine(fixture.Root, "broken.xml");
            await File.WriteAllTextAsync(xml, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<SSTXMLRessources>\n<Params><Addon>Test</Addon></Params>\n<Content>\n<String List=\"0\" sID=\"000001\"><EDID>A</EDID><REC>WEAP:FULL</REC><Source>Iron Sword</Source><Dest>철 검</Dest></String>\n<Str");

            await fixture.Vm.OpenDroppedFileAsync(xml);

            Assert.False(fixture.Vm.IsProjectLoaded);
            Assert.Contains("XML", fixture.Vm.StatusMessage);
            Assert.Contains("6", fixture.Vm.StatusMessage);
            Assert.DoesNotContain("Unexpected end of file", fixture.Vm.StatusMessage);
        });

    /// <summary>An XML that is not an xTranslator file (a fomod ModuleConfig.xml) also ended as E999.</summary>
    [Fact]
    public Task OpeningAnotherKindOfXml_SaysItIsNotAnXTranslatorFile()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var xml = Path.Combine(fixture.Root, "ModuleConfig.xml");
            await File.WriteAllTextAsync(xml, "<?xml version=\"1.0\"?><config><moduleName>Test</moduleName></config>");

            await fixture.Vm.OpenDroppedFileAsync(xml);

            Assert.False(fixture.Vm.IsProjectLoaded);
            Assert.Contains("xTranslator", fixture.Vm.StatusMessage);
            Assert.DoesNotContain("E999", fixture.Vm.StatusMessage);
        });
}
