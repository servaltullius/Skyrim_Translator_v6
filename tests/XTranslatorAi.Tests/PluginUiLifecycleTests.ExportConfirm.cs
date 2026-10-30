using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Tests;

/// <summary>Saving with rows in error or pending used to finish silently; it now asks first.</summary>
public sealed partial class PluginUiLifecycleTests
{
    [Fact]
    public Task ExportingXml_WithUnfinishedRows_AsksAndCanBeDeclined()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var rows = await LoadXmlWorkspaceAsync(fixture, "Iron Sword", "Steel Sword");
            rows[0].Status = StringEntryStatus.Error;
            var exportPath = Path.Combine(fixture.Root, "out.xml");
            fixture.Ui.SavePath = exportPath;

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.ExportXmlCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains("저장 확인", fixture.Ui.Titles);
            // An XML keeps the translation field, so an untranslated row goes out empty, not as its source.
            Assert.Contains(fixture.Ui.Messages, m => m.Contains("번역란이 지금 그대로") && !m.Contains("원문"));
            Assert.False(File.Exists(exportPath));
            Assert.StartsWith("저장하지 않았습니다", fixture.Vm.StatusMessage);
        });

    [Fact]
    public Task ExportingPlugin_WithUnfinishedRows_SaysTheSourceStays()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            Assert.Equal(StringEntryStatus.Pending, Assert.Single(fixture.Vm.Entries).Status);
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains(fixture.Ui.Messages, m => m.Contains("원문이 그대로 남습니다"));
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
        });

    /// <summary>A line agent mode held is 건너뜀 and keeps its English: the save asked nothing of it.</summary>
    [Fact]
    public Task ExportingPlugin_WithALineAgentModeHeld_SaysItStaysEnglish()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            await fixture.State.Db!.HoldStringsAsync(new[] { (row.Id, "시험") }, CancellationToken.None);
            row.Status = StringEntryStatus.Skipped;
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            fixture.Ui.Responses.Enqueue(UiMessageBoxResult.No);
            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains(fixture.Ui.Messages, m => m.Contains("보류한") && m.Contains("1개") && !m.Contains("끝나지 않은"));
            Assert.Equal("저장하지 않았습니다. 상태 필터에서 '건너뜀' 행을 확인하세요.", fixture.Vm.StatusMessage);
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
        });

    /// <summary>
    /// A translation the writer refuses for its tags was told as a model's answer breaking a token rule ("자동 복구를
    /// 켜고 다시 시도하세요"), naming no row.
    /// </summary>
    [Fact]
    public Task ExportingPlugin_WithATagTheSourceLacks_NamesTheRow()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            await fixture.State.Db!.UpdateStringTranslationAsync(row.Id, "철검 <Alias=Player>", StringEntryStatus.Edited, null, CancellationToken.None);
            row.DestText = "철검 <Alias=Player>";
            row.Status = StringEntryStatus.Edited;
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains($"#{row.OrderIndex} 행의 번역이 원문의 태그·토큰과 맞지 않아", fixture.Vm.StatusMessage);
            Assert.Contains($"row:{row.OrderIndex}", fixture.Vm.StatusMessage);
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
        });

    /// <summary>
    /// A plugin takes no empty translation: the save asked to go on ("이 행들은 빈 글로 저장됩니다"), had a folder chosen,
    /// and then failed. It stops before the folder, saying why.
    /// </summary>
    [Fact]
    public Task ExportingPlugin_WithAnEmptiedRow_StopsBeforeTheFolder()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var row = Assert.Single(fixture.Vm.Entries);
            await fixture.State.Db!.UpdateStringTranslationAsync(row.Id, "", StringEntryStatus.Edited, null, CancellationToken.None);
            row.DestText = "";
            row.Status = StringEntryStatus.Edited;
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Contains(fixture.Ui.Messages, m => m.Contains("ESP에는 빈 번역을 저장할 수 없습니다") && m.Contains($"#{row.OrderIndex}")
                                                      && !m.Contains("빈 글로 저장됩니다"));
            Assert.Contains($"row:{row.OrderIndex}", fixture.Vm.StatusMessage);
            Assert.Equal(0, fixture.Ui.SaveDialogs);
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
        });

    /// <summary>A character the output encoding lacks was named by its record alone.</summary>
    [Fact]
    public Task ExportingPlugin_WithACharacterTheEncodingLacks_NamesTheRow()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync(targetEncoding: "windows-1252");
            var row = Assert.Single(fixture.Vm.Entries);
            await fixture.State.Db!.UpdateStringTranslationAsync(row.Id, "철검", StringEntryStatus.Edited, null, CancellationToken.None);
            row.DestText = "철검";
            row.Status = StringEntryStatus.Edited;
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.StartsWith("플러그인 저장(E457):", fixture.Vm.StatusMessage);
            Assert.Contains($"관련 행: #{row.OrderIndex} (Search에 row:{row.OrderIndex})", fixture.Vm.StatusMessage);
        });

    /// <summary>The question reads the held lines' notes: a DB that fails there ended the command with the crash dialog.</summary>
    [Fact]
    public Task ExportingPlugin_WhenTheQuestionCannotReadTheDb_SaysSoAndSavesNothing()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");
            await fixture.State.Db!.DisposeAsync();

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.StartsWith("저장 확인(", fixture.Vm.StatusMessage);
            Assert.DoesNotContain("저장 확인", fixture.Ui.Titles);
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
        });
}
