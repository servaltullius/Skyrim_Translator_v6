using System.Text.Json;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task PreviousTranslation_IsLinkedByRecord_AndSentAsReferenceWhenTranslating()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "철검" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            var oldRelease = Path.Combine(fixture.Root, "old", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin("강철 장검"));
            fixture.Ui.OpenPath = oldRelease;

            Assert.True(fixture.Vm.ImportPreviousTranslationCommand.CanExecute(null));
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("강철 장검", row.PreviousTranslation);
            Assert.True(fixture.Vm.HasPreviousTranslation);
            Assert.Contains("1행", fixture.Vm.PreviousTranslationSummary);

            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            var request = Assert.Single(handler.Requests);
            Assert.Contains("Earlier translation: 강철 장검", PromptText(request.Body));
            // The reference informs the model; the saved translation is still the model's answer.
            Assert.Equal((StringEntryStatus.Done, "철검"), (row.Status, row.DestText));

            await fixture.Vm.ClearPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(row.PreviousTranslation);
            Assert.False(fixture.Vm.HasPreviousTranslation);
        });

    [Fact]
    public Task PreviousTranslation_WithNoMatchingRecords_LinksNothing()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var untranslated = Path.Combine(fixture.Root, "old", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(untranslated)!);
            await File.WriteAllBytesAsync(untranslated, PluginProjectIntegrationTests.CreateMinimalPlugin());
            fixture.Ui.OpenPath = untranslated;

            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(fixture.Vm.HasPreviousTranslation);
            Assert.Contains("연결하지 못했습니다", fixture.Vm.StatusMessage);
        });

    /// <summary>The prompt parts of a generateContent body; the JSON escapes non-ASCII text.</summary>
    private static string PromptText(string body)
    {
        using var json = JsonDocument.Parse(body);
        return string.Join("\n", json.RootElement.GetProperty("contents").EnumerateArray()
            .SelectMany(content => content.GetProperty("parts").EnumerateArray())
            .Select(part => part.GetProperty("text").GetString()));
    }
}
