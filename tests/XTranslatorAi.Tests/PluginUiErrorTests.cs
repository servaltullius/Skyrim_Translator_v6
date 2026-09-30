using System.Text;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Models;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

public sealed partial class PluginUiLifecycleTests
{
    [Theory]
    [InlineData("missing-table", "E451", "STRINGS", "Strings 폴더")]
    [InlineData("source-encoding", "E452", "WEAP:FULL/00000800", "원문 인코딩")]
    [InlineData("metadata-encoding", "E452", "MAST", "메타데이터 인코딩")]
    public Task PluginOpenValidation_ShowsActionableReason_AndKeepsExistingWorkspace(string scenario, string code, string identifier, string action)
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync();
            var originalDb = fixture.State.Db;
            var originalRow = Assert.Single(fixture.Vm.Entries);
            var brokenPath = Path.Combine(fixture.Root, "Broken.esp");
            var broken = scenario switch
            {
                "missing-table" => Header(localized: true).Concat(Group(Record("WEAP", 0x800, Sub("FULL", UInt(42))))).ToArray(),
                "source-encoding" => Header().Concat(Group(Record("WEAP", 0x800, Sub("FULL", new byte[] { 0xff, 0 })))).ToArray(),
                _ => Header(masterBytes: new byte[] { 0xff, 0 }).Concat(Group(Record("WEAP", 0x800, Sub("FULL", Z("Iron Sword"))))).ToArray(),
            };
            await File.WriteAllBytesAsync(brokenPath, broken);
            if (scenario == "metadata-encoding") fixture.Vm.PluginMetadataEncoding = "utf-8";
            fixture.Ui.OpenPath = brokenPath;

            await fixture.Vm.OpenPluginCommand.ExecuteAsync(null);

            Assert.Contains(code, fixture.Vm.StatusMessage);
            Assert.Contains(identifier, fixture.Vm.StatusMessage);
            Assert.Contains(action, fixture.Vm.StatusMessage);
            Assert.Same(originalDb, fixture.State.Db);
            Assert.Same(originalRow, Assert.Single(fixture.Vm.Entries));
            Assert.True(fixture.Vm.IsProjectLoaded);
            Assert.False(fixture.Vm.IsPluginIoBusy);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.True(fixture.Vm.ExportPluginCommand.CanExecute(null));
            Assert.False(fixture.Vm.ExportXmlCommand.CanExecute(null));
        });

    [Theory]
    [InlineData("empty-target", "E454", "WEAP:FULL/00000800")]
    [InlineData("source-changed", "E455", "다시 열어")]
    [InlineData("output-exists", "새 폴더", "출력")]
    [InlineData("target-encoding", "E457", "출력 인코딩")]
    public Task PluginSaveValidation_ShowsReason_AndRecoversBusyWithoutPublishing(string scenario, string code, string detail)
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            await fixture.LoadPluginWorkspaceAsync(targetEncoding: scenario == "target-encoding" ? "windows-1252" : "utf-8");
            var db = fixture.State.Db!;
            var document = fixture.State.PluginDocument!;
            var row = Assert.Single(fixture.Vm.Entries);
            var output = Path.Combine(fixture.Root, "translated");
            fixture.Ui.SavePath = output;
            if (scenario == "empty-target")
                await db.UpdateStringTranslationAsync(row.Id, "", StringEntryStatus.Edited, null, CancellationToken.None);
            else if (scenario == "target-encoding")
                await db.UpdateStringTranslationAsync(row.Id, "철검", StringEntryStatus.Edited, null, CancellationToken.None);
            else if (scenario == "source-changed")
                await File.AppendAllTextAsync(document.Info.InputPath, "changed");
            else
            {
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "keep.txt"), "existing output");
            }
            var before = await File.ReadAllBytesAsync(document.Info.InputPath);
            var entriesBefore = await db.GetStringsAsync(10, 0, CancellationToken.None);

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null);

            Assert.Contains(code, fixture.Vm.StatusMessage);
            Assert.Contains(detail, fixture.Vm.StatusMessage);
            Assert.False(fixture.Vm.IsPluginIoBusy);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.True(fixture.Vm.ExportPluginCommand.CanExecute(null));
            Assert.Equal(before, await File.ReadAllBytesAsync(document.Info.InputPath));
            Assert.Equal(entriesBefore, await db.GetStringsAsync(10, 0, CancellationToken.None));
            if (scenario == "output-exists")
                Assert.Equal("existing output", await File.ReadAllTextAsync(Path.Combine(output, "keep.txt")));
            else Assert.False(Directory.Exists(output));
        });

    [Fact]
    public Task PluginSaveSharedStringConflict_ReportsTableAndId_AndPreservesProject()
        => RunOnSta(async () =>
        {
            await using var fixture = new Fixture();
            var plugin = Header(localized: true).Concat(Group(
                Record("WEAP", 0x800, Sub("FULL", UInt(42))), Record("ARMO", 0x801, Sub("FULL", UInt(42))))).ToArray();
            await fixture.LoadPluginWorkspaceAsync(plugin, path =>
            {
                var folder = Path.Combine(Path.GetDirectoryName(path)!, "Strings");
                Directory.CreateDirectory(folder);
                var text = Encoding.UTF8.GetBytes("Shared\0");
                var table = UInt(1).Concat(UInt((uint)text.Length)).Concat(UInt(42)).Concat(UInt(0)).Concat(text).ToArray();
                File.WriteAllBytes(Path.Combine(folder, "Test_english.STRINGS"), table);
            });
            var db = fixture.State.Db!;
            var rows = await db.GetStringsAsync(10, 0, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows[0].Id, "공유 이름 번역", StringEntryStatus.Edited, null, CancellationToken.None);
            fixture.Ui.SavePath = Path.Combine(fixture.Root, "translated");

            await fixture.Vm.ExportPluginCommand.ExecuteAsync(null);

            Assert.Contains("E453", fixture.Vm.StatusMessage);
            Assert.Contains("Strings StringID 42", fixture.Vm.StatusMessage);
            Assert.Contains("string:STRINGS/42", fixture.Vm.StatusMessage);
            Assert.Contains("#0, #1", fixture.Vm.StatusMessage);
            Assert.Contains("일치", fixture.Vm.StatusMessage);
            Assert.False(fixture.Vm.IsPluginIoBusy);
            Assert.True(fixture.Vm.IsWorkspaceInteractive);
            Assert.True(fixture.Vm.ExportPluginCommand.CanExecute(null));
            Assert.False(Directory.Exists(fixture.Ui.SavePath));
            Assert.Equal("공유 이름 번역", (await db.GetStringsAsync(10, 0, CancellationToken.None))[0].DestText);
        });

    [Fact]
    public void PluginErrorAllowlist_DoesNotExposeArbitraryMessagesPathsOrSecrets()
    {
        const string secret = "fixture-private-payload";
        Exception[] unsupported =
        {
            new Exception(secret), new IOException(secret), new InvalidDataException(secret),
            new InvalidDataException("비어 있는 번역을 저장할 수 없습니다: " + secret),
            new Exception("비어 있는 번역을 저장할 수 없습니다: WEAP:FULL/00000800"),
        };
        foreach (var exception in unsupported)
        {
            Assert.Null(PluginUserFacingErrorClassifier.Classify(exception));
            Assert.DoesNotContain(secret, UserFacingErrorClassifier.Classify(exception).Message);
        }
        Exception[] recognized =
        {
            new EncoderFallbackException(secret),
            new NotSupportedException("지원하지 않는 플러그인 인코딩: " + secret),
            new InvalidDataException("불러온 뒤 원본이 변경되었습니다. 다시 열어주세요: " + secret),
            new FileNotFoundException(secret + "에 필요한 STRINGS 원문 테이블이 없습니다. Strings 폴더 또는 BSA 경로를 확인하세요."),
            new InvalidDataException("여러 필드가 같은 Strings StringID 42를 공유하지만 번역이 다릅니다. " + secret),
        };
        foreach (var exception in recognized)
        {
            var mapped = PluginUserFacingErrorClassifier.Classify(exception);
            Assert.NotNull(mapped);
            Assert.DoesNotContain(secret, mapped.Value.Message);
            Assert.False(mapped.Value.DetailsInApiLogs);
        }
    }
}
