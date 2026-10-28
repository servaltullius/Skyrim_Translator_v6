using XTranslatorAi.App.Services;
using System.Text.Json;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

public sealed partial class PluginUiLifecycleTests
{
    // An older Korean release saved in CP949 was refused (E452, choose CP949) and had to be saved again as UTF-8.
    [Fact]
    public Task PreviousTranslation_SavedInCp949_IsLinked()
        => RunOnSta(async () =>
        {
            static byte[] Sword(byte[] name) => PluginReadWriteTests.Header().Concat(PluginReadWriteTests.Group(PluginReadWriteTests.Record("WEAP", 0x800,
                PluginReadWriteTests.Sub("EDID", PluginReadWriteTests.Z("TestSword")), PluginReadWriteTests.Sub("FULL", name)))).ToArray();
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync(Sword(PluginReadWriteTests.Z("Iron Sword")));
            var row = Assert.Single(fixture.Vm.Entries);
            var oldRelease = Path.Combine(fixture.Root, "old", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            await File.WriteAllBytesAsync(oldRelease, Sword(XTranslatorAi.Core.Plugins.PluginBinary.GetEncoding("ks_c_5601-1987").GetBytes("강철 장검\0")));
            fixture.Ui.OpenPath = oldRelease;

            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("CP949로 읽음", fixture.Vm.StatusMessage);
            Assert.Equal("강철 장검", row.PreviousTranslation);
        });

    // A release neither UTF-8 nor CP949 reads was told as "예상치 못한 오류가 발생했습니다"(E999): the earlier-release
    // tools used the general classifier, not the plugin one.
    [Fact]
    public Task PreviousTranslation_ThatCannotBeRead_IsToldAsAPluginError()
        => RunOnSta(async () =>
        {
            static byte[] Sword(byte[] name) => PluginReadWriteTests.Header().Concat(PluginReadWriteTests.Group(PluginReadWriteTests.Record("WEAP", 0x800,
                PluginReadWriteTests.Sub("EDID", PluginReadWriteTests.Z("TestSword")), PluginReadWriteTests.Sub("FULL", name)))).ToArray();
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync(Sword(PluginReadWriteTests.Z("Iron Sword")));
            var oldRelease = Path.Combine(fixture.Root, "old", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            await File.WriteAllBytesAsync(oldRelease, PluginReadWriteTests.Header().Concat(PluginReadWriteTests.Group(
                PluginReadWriteTests.Record("WEAP", 0x800, PluginReadWriteTests.Sub("FULL",
                    XTranslatorAi.Core.Plugins.PluginBinary.GetEncoding("ks_c_5601-1987").GetBytes("강철 장검\0"))),
                PluginReadWriteTests.Record("WEAP", 0x801, PluginReadWriteTests.Sub("FULL", new byte[] { 0x41, 0x81, 0 }))))
                .ToArray());
            fixture.Ui.OpenPath = oldRelease;

            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("E452", fixture.Vm.StatusMessage);
            Assert.Contains("CP949", fixture.Vm.StatusMessage);
        });

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

    /// <summary>
    /// With the earlier release's English linked too, a row whose English did not change keeps the earlier
    /// translation as it is, without a request; a row whose English changed is translated with it as a reference.
    /// </summary>
    [Theory]
    [InlineData("Iron Sword", true)]
    [InlineData("Steel Sword", false)]
    public Task PreviousSource_ReusesTheEarlierTranslationOnlyWhereTheEnglishIsTheSame(string earlierEnglish, bool reused)
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "철검" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            var oldRelease = Path.Combine(fixture.Root, "old-ko", "Test.esp");
            var oldEnglish = Path.Combine(fixture.Root, "old-en", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldEnglish)!);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin("강철 장검"));
            await File.WriteAllBytesAsync(oldEnglish, PluginProjectIntegrationTests.CreateMinimalPlugin(earlierEnglish));

            Assert.False(fixture.Vm.ImportPreviousSourceCommand.CanExecute(null));
            fixture.Ui.OpenPath = oldRelease;
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Ui.OpenPath = oldEnglish;
            Assert.True(fixture.Vm.ImportPreviousSourceCommand.CanExecute(null));
            await fixture.Vm.ImportPreviousSourceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains($"{(reused ? 1 : 0)}행은 다음 번역 때 AI 없이", fixture.Vm.StatusMessage);
            Assert.Contains("영어판 Test.esp", fixture.Vm.PreviousTranslationSummary);

            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            if (reused)
            {
                Assert.Empty(handler.Requests);
                Assert.Equal((StringEntryStatus.Done, "강철 장검"), (row.Status, row.DestText));
            }
            else
            {
                Assert.Contains("Earlier translation: 강철 장검", PromptText(Assert.Single(handler.Requests).Body));
                Assert.Equal((StringEntryStatus.Done, "철검"), (row.Status, row.DestText));
            }

            // Kept without a request, the row keeps the TM color after the run ends, as a TM hit does.
            Assert.Equal(reused, row.IsTranslationMemoryApplied);

            // Linking another earlier translation drops the English that belonged to the last one.
            fixture.Ui.OpenPath = oldRelease;
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.DoesNotContain("영어판", fixture.Vm.PreviousTranslationSummary);
        });

    // The earlier English was read with the source encoding now in the settings, which apply to the next ESP opened:
    // changed to ks_c_5601-1987 for another file, a UTF-8 "Dragon’s Bane" read as Korean and was refused.
    [Fact]
    public Task PreviousSource_IsReadWithTheEncodingTheProjectWasOpenedWith()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture(new FakeGeminiHandler { GeneratedText = "용의 재앙" });
            await fixture.LoadPluginWorkspaceAsync();
            // Opened as the window opens a plugin, which keeps the encoding chosen then.
            var current = Path.Combine(fixture.Root, "current", "Test.esp");
            var oldRelease = Path.Combine(fixture.Root, "old-ko", "Test.esp");
            var oldEnglish = Path.Combine(fixture.Root, "old-en", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldEnglish)!);
            await File.WriteAllBytesAsync(current, PluginProjectIntegrationTests.CreateMinimalPlugin("Dragon’s Bane"));
            fixture.Ui.OpenPath = current;
            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Dragon’s Bane", Assert.Single(fixture.Vm.Entries).SourceText);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin("용의 재앙"));
            await File.WriteAllBytesAsync(oldEnglish, PluginProjectIntegrationTests.CreateMinimalPlugin("Dragon’s Bane"));
            fixture.Ui.OpenPath = oldRelease;
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            fixture.Vm.PluginSourceEncoding = "ks_c_5601-1987";
            fixture.Ui.OpenPath = oldEnglish;
            await fixture.Vm.ImportPreviousSourceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("1행은 다음 번역 때 AI 없이", fixture.Vm.StatusMessage);
        });

    /// <summary>
    /// A text that starts or ends with a line break or a space (a book, an effect description) keeps it: linking trimmed
    /// the earlier translation, so the reused row lost its spaces, or failed the line break check and went to the model.
    /// </summary>
    [Theory]
    [InlineData("Iron Sword\r\n", "강철 장검\r\n")]
    [InlineData("\r\nIron Sword\r\n\r\n", "\r\n강철 장검\r\n\r\n")]
    [InlineData(" Iron Sword ", " 강철 장검 ")]
    public Task PreviousSource_ReusedTranslation_KeepsTheLineBreaksAndSpacesAroundIt(string english, string earlier)
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler { GeneratedText = "철검" };
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync(PluginProjectIntegrationTests.CreateMinimalPlugin(english));
            var row = Assert.Single(fixture.Vm.Entries);
            var oldRelease = Path.Combine(fixture.Root, "old-ko", "Test.esp");
            var oldEnglish = Path.Combine(fixture.Root, "old-en", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldEnglish)!);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin(earlier));
            await File.WriteAllBytesAsync(oldEnglish, PluginProjectIntegrationTests.CreateMinimalPlugin(english));
            fixture.Ui.OpenPath = oldRelease;
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Ui.OpenPath = oldEnglish;
            await fixture.Vm.ImportPreviousSourceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            await fixture.Vm.StartTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(handler.Requests);
            Assert.Equal((StringEntryStatus.Done, earlier), (row.Status, row.DestText));
            var saved = Assert.Single(await fixture.State.Db!.GetStringsAsync(10, 0, CancellationToken.None));
            Assert.Equal(earlier, saved.DestText);
        });

    [Fact]
    public Task PreviousSource_RowsKeptAsTheyAre_AreLeftOutOfTheCostEstimate()
        => RunOnSta(async () =>
        {
            var handler = new FakeGeminiHandler();
            await using var fixture = new Fixture(handler);
            await fixture.LoadPluginWorkspaceAsync();
            var oldRelease = Path.Combine(fixture.Root, "old-ko", "Test.esp");
            var oldEnglish = Path.Combine(fixture.Root, "old-en", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            Directory.CreateDirectory(Path.GetDirectoryName(oldEnglish)!);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin("강철 장검"));
            await File.WriteAllBytesAsync(oldEnglish, PluginProjectIntegrationTests.CreateMinimalPlugin());
            fixture.Ui.OpenPath = oldRelease;
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Ui.OpenPath = oldEnglish;
            await fixture.Vm.ImportPreviousSourceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No); // Remaining rows.
            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No); // countTokens only.
            await fixture.Vm.EstimateCostCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.DoesNotContain(handler.Requests, request => request.Body.Contains("Iron Sword", StringComparison.Ordinal));
            Assert.Contains("요청 없이 채울 행: 1 (게임 번역 0, 이전 번역 그대로 1)", fixture.Ui.Messages[^1]);
        });

    [Fact]
    public Task PreviousSource_ThatIsATranslatedRelease_IsRefused()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var oldRelease = Path.Combine(fixture.Root, "old-ko", "Test.esp");
            Directory.CreateDirectory(Path.GetDirectoryName(oldRelease)!);
            await File.WriteAllBytesAsync(oldRelease, PluginProjectIntegrationTests.CreateMinimalPlugin("강철 장검"));
            fixture.Ui.OpenPath = oldRelease;
            await fixture.Vm.ImportPreviousTranslationCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            await fixture.Vm.ImportPreviousSourceCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("한국어로 번역된 파일", fixture.Vm.StatusMessage);
            Assert.DoesNotContain("영어판", fixture.Vm.PreviousTranslationSummary);
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
