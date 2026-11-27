using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;
using Xunit;

namespace XTranslatorAi.Tests;

/// <summary>
/// Moved plugin behavior in agent mode (L7): writes stop before opening the DB when a project's plugin is no longer
/// in place, reads keep working with a warning, name lookup prefers the one project whose plugin is in place, and a
/// round-trip move warns when a missing project's DB was written later.
/// </summary>
public sealed class AgentMovedPluginTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xt-agent-moved", Guid.NewGuid().ToString("N"));
    private string _workRoot = null!;
    private string _projectsRoot = null!;
    private string _globalRoot = null!;
    private GlobalProjectDbService _global = null!;
    private AgentSession _session = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _workRoot = Path.Combine(_root, "work");
        _projectsRoot = Path.Combine(_root, "projects");
        _globalRoot = Path.Combine(_root, "global");
        _global = new GlobalProjectDbService(new BuiltInGlossaryService(), _globalRoot);
        _session = CreateSession();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _global.DisposeAsync();
        if (Directory.Exists(_root))
        {
            foreach (var db in Directory.EnumerateFiles(_root, "*.sqlite", SearchOption.AllDirectories))
            {
                ProjectDb.ReleasePool(db);
            }
        }

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private AgentSession CreateSession()
        => new(_workRoot, _global, gameStrings: null, new AppSettings(), projectsRootOverride: _projectsRoot);

    private string WritePlugin(string folder, string fileName = "Test.esp", string itemName = "Iron Sword")
    {
        var path = Path.Combine(_root, folder, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PluginProjectIntegrationTests.CreateMinimalPlugin(itemName));
        return path;
    }

    private static string MovePlugin(string fromPath, string toPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(toPath)!);
        File.Move(fromPath, toPath);
        return toPath;
    }

    private async Task<ProjectWorkspaceService.LoadFromPluginResult> OpenInGuiAsync(string pluginPath, string targetEncoding = "utf-8")
    {
        var workspace = new ProjectWorkspaceService(_global, _projectsRoot);
        return await workspace.LoadFromPluginAsync(new ProjectWorkspaceService.LoadFromPluginRequest(
            pluginPath, new PluginReadOptions(), "korean", targetEncoding, "gemini-3.8-flash", "", false), CancellationToken.None);
    }

    private async Task<(int ExitCode, string Output)> RunCliAsync(params string[] commandArgs)
    {
        var fullArgs = commandArgs.Concat(new[] { "--work", _workRoot, "--projects", _projectsRoot, "--global", _globalRoot }).ToArray();
        var captured = new StringWriter();
        var exit = await AgentCli.RunAsync(fullArgs, argumentFiles: false, captured);
        return (exit, captured.ToString());
    }

    private static async Task<Dictionary<string, List<string>>> SnapshotDbTablesAsync(string dbPath)
    {
        var snapshot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        await conn.OpenAsync();
        var tables = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            var rows = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\";";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var cols = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    cols[i] = reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                }

                rows.Add(string.Join("\t", cols));
            }

            rows.Sort(StringComparer.Ordinal);
            snapshot[table] = rows;
        }

        return snapshot;
    }

    private static Dictionary<string, byte[]> SnapshotFolder(string folder)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return files;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            files[Path.GetRelativePath(folder, file)] = File.ReadAllBytes(file);
        }

        return files;
    }

    private static void AssertSameFolder(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase), actual.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        foreach (var (rel, bytes) in expected)
        {
            Assert.Equal(bytes, actual[rel]);
        }
    }

    private static void AssertSameTables(Dictionary<string, List<string>> expected, Dictionary<string, List<string>> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (table, rows) in expected)
        {
            Assert.Equal(rows, actual[table]);
        }
    }

    private static void SetDbWriteTimeUtc(string dbPath, DateTime utc)
    {
        ProjectDb.ReleasePool(dbPath);
        if (File.Exists(dbPath)) File.SetLastWriteTimeUtc(dbPath, utc);
        var wal = dbPath + "-wal";
        if (File.Exists(wal)) File.SetLastWriteTimeUtc(wal, utc);
    }

    // T1: Every project-taking CLI command run against a moved plugin (Repro A): writes fail before opening the DB,
    // reporting the missing plugin path, and neither the old DB's tables nor the project's work folder change.
    [Fact]
    public async Task T1_WriteCommands_RefuseMovedPlugin_WithoutTouchingOldDbOrWorkFolder()
    {
        var v1 = WritePlugin("v1");
        var (project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        // Make a waiting request before moving so submit/hold/context/terms/style would all have pending state to touch.
        var step = await _session.NextAsync(project, CancellationToken.None);
        Assert.NotNull(step.Next);

        long rowId;
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None))
        {
            rowId = Assert.Single(await db.GetAllStringsAsync(CancellationToken.None)).Id;
        }

        var v2 = MovePlugin(v1, Path.Combine(_root, "v2", "Test.esp"));

        var answerFile = Path.Combine(_root, "answer.txt");
        await File.WriteAllTextAsync(answerFile, JsonSerializer.Serialize(new { translations = new[] { new { id = rowId, text = "철검" } } }), new UTF8Encoding(false));
        var editFile = Path.Combine(_root, "edit.json");
        await File.WriteAllTextAsync(editFile, JsonSerializer.Serialize(new[] { new { id = rowId, text = "강철검" } }), new UTF8Encoding(false));
        var contextFile = Path.Combine(_root, "context.txt");
        await File.WriteAllTextAsync(contextFile, "스카이림 시대 배경", new UTF8Encoding(false));
        var termsFile = Path.Combine(_root, "terms.json");
        await File.WriteAllTextAsync(termsFile, """[{"source":"Iron Sword","target":"철검"}]""", new UTF8Encoding(false));
        var koreanFile = WritePlugin("korean-release", "Test.esp", "철검");
        var exportDir = Path.Combine(_root, "export-out");

        var projectWorkDir = Path.Combine(_workRoot, project.Key);

        var writeCommands = new[]
        {
            new[] { "next", project.Key },
            new[] { "submit", project.Key, answerFile },
            new[] { "review", project.Key, "--id", rowId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new[] { "export", project.Key, exportDir },
            new[] { "edit", project.Key, editFile },
            new[] { "hold", project.Key, "--id", rowId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--note", "사유" },
            new[] { "names", project.Key, "--save" },
            new[] { "style", project.Key, "player", "반말", "--to", "other" },
            new[] { "context", project.Key, contextFile },
            new[] { "context", project.Key, "--clear" },
            new[] { "terms", project.Key, termsFile },
            new[] { "terms", project.Key, "Iron Sword", "--clear" },
            new[] { "korean", project.Key, koreanFile },
        };

        // Nothing has opened the plugin in its new place yet, so the old DB is still there: each write is refused at
        // its own command (one taken for a read would open the DB and write), and nothing changes.
        var liveDbBefore = await SnapshotDbTablesAsync(project.DbPath);
        var liveWorkBefore = SnapshotFolder(projectWorkDir);
        foreach (var args in writeCommands)
        {
            var (code, output) = await RunCliAsync(args);
            Assert.True(code != 0, $"Expected failure for '{string.Join(' ', args)}', got exit {code}:\n{output}");
            // The refusal itself: the read warning says "플러그인이 '" too, and export and korean failed further on anyway.
            Assert.StartsWith("실패: 플러그인이 '", output);
            Assert.Contains(v1, output);
        }

        Assert.False(Directory.Exists(exportDir));
        AssertSameTables(liveDbBefore, await SnapshotDbTablesAsync(project.DbPath));
        AssertSameFolder(liveWorkBefore, SnapshotFolder(projectWorkDir));

        // The GUI opens it there: the project goes with the file, and the old DB is kept aside.
        await using (var guiOpened = (await OpenInGuiAsync(v2)).Db) { }
        var archivedDbPath = project.DbPath + ".moved";
        Assert.False(File.Exists(project.DbPath));
        Assert.True(File.Exists(archivedDbPath));
        var dbBefore = await SnapshotDbTablesAsync(archivedDbPath);
        var workBefore = SnapshotFolder(projectWorkDir);

        foreach (var args in writeCommands)
        {
            var (code, output) = await RunCliAsync(args);
            Assert.True(code != 0, $"Expected failure for '{string.Join(' ', args)}', got exit {code}:\n{output}");
            Assert.StartsWith("실패: 플러그인이 '", output);
            Assert.Contains(v1, output);
            Assert.False(File.Exists(project.DbPath));
        }

        Assert.False(Directory.Exists(exportDir));

        var readCommands = new[]
        {
            new[] { "status", project.Key },
            new[] { "check", project.Key },
            new[] { "show", project.Key },
            new[] { "lookup", project.Key, "Iron" },
            new[] { "names", project.Key },
            new[] { "voice", project.Key, "Lydia" },
            new[] { "style", project.Key },
            new[] { "context", project.Key },
            new[] { "terms", project.Key },
            new[] { "hold", project.Key },
        };

        // Once the GUI opened v2 and retired v1's DB to .moved, reads against the old key also refuse without creating a DB.
        foreach (var args in readCommands)
        {
            var (code, output) = await RunCliAsync(args);
            Assert.True(code != 0, $"Expected failure for '{string.Join(' ', args)}', got exit {code}:\n{output}");
            Assert.StartsWith("실패: 플러그인이 '", output);
            Assert.Contains(v1, output);
            Assert.False(File.Exists(project.DbPath));
        }

        var dbAfter = await SnapshotDbTablesAsync(archivedDbPath);
        var workAfter = SnapshotFolder(projectWorkDir);
        AssertSameTables(dbBefore, dbAfter);
        AssertSameFolder(workBefore, workAfter);
    }

    // T2: While a moved plugin has not yet been opened at its new location (so the old DB is still in place),
    // status, show and check succeed with a missing-plugin warning, projects marks it, and MCP request still works.
    // Once opened at the new location (retiring the old DB), reads on the old key fail cleanly without creating a DB.
    [Fact]
    public async Task T2_ReadCommands_SucceedWithMissingPluginWarning_AndProjectsMarksMissing()
    {
        var v1 = WritePlugin("v1");
        var (project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        var v2 = MovePlugin(v1, Path.Combine(_root, "v2", "Test.esp"));

        foreach (var cmd in new[] { "status", "show", "check" })
        {
            var (code, output) = await RunCliAsync(cmd, project.Key);
            Assert.Equal(0, code);
            Assert.StartsWith("경고: 플러그인이 '", output);
            Assert.Contains(v1, output);
            Assert.Contains("읽기만 됩니다", output);
        }

        var (projCode, projOutput) = await RunCliAsync("projects");
        Assert.Equal(0, projCode);
        Assert.Contains($"{project.Key}\t{v1}\t(플러그인 없음)", projOutput);

        // MCP request tool uses session.Find directly and must not throw when the plugin is missing.
        var server = AgentCommand.Parse(new List<string> { "--work", _workRoot, "--projects", _projectsRoot, "--global", _globalRoot });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "request", arguments = new { project = project.Key } },
        });
        var mcpOut = new StringWriter();
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), mcpOut));
        var reply = JsonDocument.Parse(mcpOut.ToString()).RootElement;
        Assert.False(reply.TryGetProperty("error", out _), mcpOut.ToString());
        Assert.Contains("대기 중인 요청이 없습니다", reply.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());

        // After GUI opens v2 and retires v1's DB, reads on the old key refuse rather than creating an empty DB.
        await using (var guiOpened = (await OpenInGuiAsync(v2)).Db) { }
        Assert.False(File.Exists(project.DbPath));
        foreach (var cmd in new[] { "status", "show", "check" })
        {
            var (code, output) = await RunCliAsync(cmd, project.Key);
            Assert.NotEqual(0, code);
            Assert.StartsWith("실패: 플러그인이 '", output);
            Assert.Contains(v1, output);
            Assert.False(File.Exists(project.DbPath));
        }
    }

    // T3: After reopening at the new path (both Repro A and Repro B), Find("Test.esp") and Find("Test") return the new
    // project; Find(oldKey) still returns the old project and write refusal names the new project's key and path.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T3_AfterReopen_NameFindReturnsNewProject_AndOldKeyWriteRefusalNamesNew(bool guiOpenedFirst)
    {
        var v1 = WritePlugin("v1");
        var (oldProject, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        var v2 = MovePlugin(v1, Path.Combine(_root, "v2", "Test.esp"));
        if (guiOpenedFirst)
        {
            await using var guiDb = (await OpenInGuiAsync(v2)).Db;
        }

        var (newProject, _, _, _) = await _session.OpenAsync(v2, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        Assert.NotEqual(oldProject.Key, newProject.Key);

        Assert.Equal(newProject.Key, _session.Find("Test.esp").Key);
        Assert.Equal(newProject.Key, _session.Find("Test").Key);
        Assert.Equal(oldProject.Key, _session.Find(oldProject.Key).Key);

        var (code, output) = await RunCliAsync("next", oldProject.Key);
        Assert.NotEqual(0, code);
        Assert.Contains("플러그인이 '", output);
        Assert.Contains(v1, output);
        Assert.Contains("같은 이름으로 열려 있는 프로젝트:", output);
        Assert.Contains(newProject.Key, output);
        Assert.Contains(v2, output);
        // A plain move, with the files' own times: the project reopened is the newer one, and the there-and-back
        // hints (which tell the agent to stop and ask) stay off.
        Assert.DoesNotContain("더 나중에 작업됐습니다", output);
        var (statusCode, statusOutput) = await RunCliAsync("status", "Test.esp");
        Assert.Equal(0, statusCode);
        Assert.DoesNotContain("더 나중에 작업됐습니다", statusOutput);
    }

    // T4: Find by the missing old full path still resolves the old project rather than saying "없습니다", and write refuses.
    [Fact]
    public async Task T4_FindByMissingOldFullPath_ReturnsOldProject_AndWriteRefuses()
    {
        var v1 = WritePlugin("v1");
        var (oldProject, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        MovePlugin(v1, Path.Combine(_root, "v2", "Test.esp"));

        var found = _session.Find(v1);
        Assert.Equal(oldProject.Key, found.Key);

        var (code, output) = await RunCliAsync("next", v1);
        Assert.NotEqual(0, code);
        Assert.Contains("플러그인이 '", output);
        Assert.Contains(v1, output);
        Assert.DoesNotContain("프로젝트가 없습니다", output);

        // Invalid path characters are treated as "not a path", not an unhandled path exception.
        var bad = Assert.Throws<AgentModeException>(() => _session.Find("C:\0bad\\Test.esp"));
        Assert.Contains("프로젝트가 없습니다", bad.Message);
    }

    // T5: Temporarily removing the plugin refuses writes without altering project.json/pending.json/request.md;
    // restoring it to the same path lets the same project (same key and DB) write again.
    [Fact]
    public async Task T5_TemporarilyMissingPlugin_RefusesWriteLeavingFilesUntouched_AndWorksWhenRestored()
    {
        var v1 = WritePlugin("v1");
        var (project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        await _session.NextAsync(project, CancellationToken.None);

        long rowId;
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None))
        {
            rowId = Assert.Single(await db.GetAllStringsAsync(CancellationToken.None)).Id;
        }

        var projectJsonPath = Path.Combine(_workRoot, project.Key, "project.json");
        var pendingPath = Path.Combine(_workRoot, project.Key, "pending.json");
        var requestPath = Path.Combine(_workRoot, project.Key, "request.md");
        var projectJsonBefore = await File.ReadAllBytesAsync(projectJsonPath);
        var pendingBefore = await File.ReadAllBytesAsync(pendingPath);
        var requestBefore = await File.ReadAllBytesAsync(requestPath);

        var aside = MovePlugin(v1, Path.Combine(_root, "aside", "Test.esp"));
        var (refusedCode, refusedOutput) = await RunCliAsync("hold", project.Key, "--id", rowId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--note", "시험");
        Assert.NotEqual(0, refusedCode);
        Assert.Contains("플러그인이 '", refusedOutput);
        Assert.Equal(projectJsonBefore, await File.ReadAllBytesAsync(projectJsonPath));
        Assert.Equal(pendingBefore, await File.ReadAllBytesAsync(pendingPath));
        Assert.Equal(requestBefore, await File.ReadAllBytesAsync(requestPath));

        MovePlugin(aside, v1);
        var (restoredCode, _) = await RunCliAsync("hold", project.Key, "--id", rowId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--note", "복구");
        Assert.Equal(0, restoredCode);
        Assert.Equal(project.Key, _session.Find("Test.esp").Key);
        Assert.Equal(project.DbPath, _session.Find("Test.esp").DbPath);
        var held = await _session.HeldAsync(project, CancellationToken.None);
        Assert.Single(held);
    }

    // T6: Continuation regression — a hold made before moving survives Repro B (agent reopen) and Repro A (GUI open then agent reopen).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T6_Continuation_PreservesHeldRow_InReproBAndReproA(bool guiOpensFirst)
    {
        var v1 = WritePlugin("v1");
        var (oldProject, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        long rowId;
        await using (var db = await ProjectDb.OpenOrCreateAsync(oldProject.DbPath, CancellationToken.None))
        {
            rowId = Assert.Single(await db.GetAllStringsAsync(CancellationToken.None)).Id;
        }

        await _session.HoldAsync(oldProject, new[] { rowId }, "보류 사유", CancellationToken.None);
        var v2 = MovePlugin(v1, Path.Combine(_root, "v2", "Test.esp"));

        string? guiDbPath = null;
        if (guiOpensFirst)
        {
            var guiResult = await OpenInGuiAsync(v2);
            guiDbPath = guiResult.Db.DbPath;
            await using (guiResult.Db)
            {
                Assert.Equal(v1, guiResult.MovedFromPath);
            }
        }

        var (newProject, _, _, _) = await _session.OpenAsync(v2, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        if (guiDbPath != null)
        {
            Assert.Equal(guiDbPath, newProject.DbPath);
        }

        var held = await _session.HeldAsync(newProject, CancellationToken.None);
        var line = Assert.Single(held);
        Assert.Equal("보류 사유", line.Reason);
    }

    // T7: Same path with different target encodings — GUI opening another encoding does not alter the agent project's DB path,
    // and two agent projects for the same path report both keys, path and encodings on name lookup.
    [Fact]
    public async Task T7_SamePathDifferentEncoding_KeepsDbPath_AndAmbiguousFindListsEncodings()
    {
        var v1 = WritePlugin("v1");
        var (utf8Project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        await using (var guiOther = (await OpenInGuiAsync(v1, targetEncoding: "ks_c_5601-1987")).Db) { }

        Assert.Equal(utf8Project.DbPath, _session.Find(utf8Project.Key).DbPath);

        var (cp949Project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "ks_c_5601-1987", batchSize: 10, CancellationToken.None);
        Assert.NotEqual(utf8Project.Key, cp949Project.Key);

        var ex = Assert.Throws<AgentModeException>(() => _session.Find("Test.esp"));
        Assert.Contains("'Test.esp'에 맞는 프로젝트가 여럿입니다. 키로 지정하세요:", ex.Message);
        Assert.Contains(utf8Project.Key, ex.Message);
        Assert.Contains(cp949Project.Key, ex.Message);
        Assert.Contains(v1, ex.Message);
        Assert.Contains("utf-8", ex.Message);
        Assert.Contains("ks_c_5601-1987", ex.Message);

        Assert.Equal(utf8Project.Key, _session.Find(utf8Project.Key).Key);
        Assert.Equal(cp949Project.Key, _session.Find(cp949Project.Key).Key);
    }

    // T8: Opening a copy while the original remains in place leaves the original's project.json untouched and makes name lookup ambiguous.
    [Fact]
    public async Task T8_CopyWithOriginalInPlace_KeepsOriginalProjectJson_AndNameFindIsAmbiguous()
    {
        var originalPath = WritePlugin("v1");
        var (original, _, _, _) = await _session.OpenAsync(originalPath, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        var origJsonPath = Path.Combine(_workRoot, original.Key, "project.json");
        var origJsonBefore = await File.ReadAllBytesAsync(origJsonPath);

        var copyPath = Path.Combine(_root, "v2", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        File.Copy(originalPath, copyPath);

        var (copy, _, _, _) = await _session.OpenAsync(copyPath, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        Assert.Equal(origJsonBefore, await File.ReadAllBytesAsync(origJsonPath));

        var ex = Assert.Throws<AgentModeException>(() => _session.Find("Test.esp"));
        Assert.Contains("'Test.esp'에 맞는 프로젝트가 여럿입니다. 키로 지정하세요:", ex.Message);
        Assert.Contains(original.Key, ex.Message);
        Assert.Contains(copy.Key, ex.Message);
        Assert.DoesNotContain("(플러그인 없음)", ex.Message);

        // When both are missing, name lookup is still ambiguous and marks each missing project.
        File.Delete(originalPath);
        File.Delete(copyPath);
        var allMissing = Assert.Throws<AgentModeException>(() => _session.Find("Test.esp"));
        Assert.Contains($"{original.Key}\t{originalPath}\tutf-8\t(플러그인 없음)", allMissing.Message);
        Assert.Contains($"{copy.Key}\t{copyPath}\tutf-8\t(플러그인 없음)", allMissing.Message);
    }

    // T9: Lane subagent refusal instructs not to open and to notify the parent.
    [Fact]
    public async Task T9_LaneWriteOnMovedPlugin_TellsSubagentNotToOpenAndReportToParent()
    {
        var v1 = WritePlugin("v1");
        var (project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        MovePlugin(v1, Path.Combine(_root, "v2", "Test.esp"));

        var (code, output) = await RunCliAsync("next", project.Key, "--lane", "1/2");
        Assert.NotEqual(0, code);
        Assert.Contains("플러그인이 '", output);
        Assert.Contains("하위 에이전트는 open하지 말고", output);
    }

    // T11: Legacy already-split projects (both A and B DBs exist, B's plugin missing) print a note on name lookup
    // while B's DB is newer, warn on writes to B, and clear the note once A is reopened and newer.
    [Fact]
    public async Task T11_RoundTripMove_NotifiesWhenMissingProjectsDbIsNewer_AndClearsAfterReopen()
    {
        var pathA = WritePlugin("A");
        var (projectA, _, _, _) = await _session.OpenAsync(pathA, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);

        // Copy (not move) so both A's DB and B's DB remain present, then remove B to reproduce an already-split state.
        var pathB = Path.Combine(_root, "B", "Test.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(pathB)!);
        File.Copy(pathA, pathB);
        var (projectB, _, _, _) = await _session.OpenAsync(pathB, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        long rowIdB;
        await using (var dbB = await ProjectDb.OpenOrCreateAsync(projectB.DbPath, CancellationToken.None))
        {
            rowIdB = Assert.Single(await dbB.GetAllStringsAsync(CancellationToken.None)).Id;
        }

        await _session.HoldAsync(projectB, new[] { rowIdB }, "B에서 작업", CancellationToken.None);
        File.Delete(pathB);

        var baseTime = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        SetDbWriteTimeUtc(projectA.DbPath, baseTime);
        SetDbWriteTimeUtc(projectB.DbPath, baseTime.AddMinutes(5));

        var (code, output) = await RunCliAsync("status", "Test.esp");
        Assert.Equal(0, code);
        Assert.Contains("참고: 같은 이름의 옮겨진 프로젝트", output);
        Assert.Contains(projectB.Key, output);
        Assert.Contains(pathB, output);

        // By key (not by name): no round-trip note.
        var (byKeyCode, byKeyOutput) = await RunCliAsync("status", projectA.Key);
        Assert.Equal(0, byKeyCode);
        Assert.DoesNotContain("참고: 같은 이름의 옮겨진 프로젝트", byKeyOutput);

        // A write to B is refused and names A, which is the older one: the refusal says so, or it sent the agent to A
        // with no word that B's lines are not there.
        var (refusedCode, refused) = await RunCliAsync("next", projectB.Key);
        Assert.NotEqual(0, refusedCode);
        Assert.Contains(projectA.Key, refused);
        Assert.Contains("이 프로젝트가 더 나중에 작업됐습니다", refused);

        // An empty -wal is no work: opening another plugin reads every project DB of the folder and leaves one beside
        // each, and the old project then read as the newer.
        SetDbWriteTimeUtc(projectB.DbPath, baseTime.AddMinutes(-5));
        var emptyWalOfB = projectB.DbPath + "-wal";
        File.WriteAllBytes(emptyWalOfB, Array.Empty<byte>());
        File.SetLastWriteTimeUtc(emptyWalOfB, baseTime.AddMinutes(30));
        var (emptyWalCode, emptyWalOutput) = await RunCliAsync("status", "Test.esp");
        Assert.Equal(0, emptyWalCode);
        Assert.DoesNotContain("참고: 같은 이름의 옮겨진 프로젝트", emptyWalOutput);
        File.Delete(emptyWalOfB);
        SetDbWriteTimeUtc(projectB.DbPath, baseTime.AddMinutes(5));

        // Reopen A and make A's DB newer: note disappears.
        await _session.OpenAsync(pathA, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        SetDbWriteTimeUtc(projectB.DbPath, baseTime);
        SetDbWriteTimeUtc(projectA.DbPath, baseTime.AddMinutes(10));

        var (afterCode, afterOutput) = await RunCliAsync("status", "Test.esp");
        Assert.Equal(0, afterCode);
        Assert.DoesNotContain("참고: 같은 이름의 옮겨진 프로젝트", afterOutput);
    }

    // "agent @args.txt" with mcp in the file was read as a command before its file was: it held the console lock, and
    // the server's first tool call waited for the lock forever.
    [Fact]
    public async Task McpServerIsToldFromACommand_AfterItsArgumentFileIsRead()
    {
        Directory.CreateDirectory(_root);
        var argsFile = Path.Combine(_root, "mcp-args.txt");
        await File.WriteAllLinesAsync(argsFile, new[] { "mcp", "--work", _workRoot }, new UTF8Encoding(false));

        Assert.True(AgentCli.IsMcpServer(new[] { "mcp", "--work", _workRoot }, argumentFiles: true));
        Assert.True(AgentCli.IsMcpServer(new[] { "@" + argsFile }, argumentFiles: true));
        Assert.False(AgentCli.IsMcpServer(new[] { "@" + argsFile }, argumentFiles: false));
        Assert.False(AgentCli.IsMcpServer(new[] { "status", "mcp" }, argumentFiles: true));
        Assert.False(AgentCli.IsMcpServer(new[] { "@" + argsFile + ".missing" }, argumentFiles: true));
        Assert.False(AgentCli.IsMcpServer(Array.Empty<string>(), argumentFiles: true));
    }

    // R2: Round-trip move in agent mode (open A -> move -> open B -> hold -> move back -> open A) preserves the hold
    // and name-based status picks the returned project.
    [Fact]
    public async Task R2_RoundTripMove_AgentOpen_PreservesHoldAndNameStatus()
    {
        var pathA = WritePlugin("A");
        var (openACode, _) = await RunCliAsync("open", pathA);
        Assert.Equal(0, openACode);

        var pathB = MovePlugin(pathA, Path.Combine(_root, "B", "Test.esp"));
        var (openBCode, _) = await RunCliAsync("open", pathB);
        Assert.Equal(0, openBCode);

        var projectB = _session.Find(pathB);
        long rowIdB;
        await using (var dbB = await ProjectDb.OpenOrCreateAsync(projectB.DbPath, CancellationToken.None))
        {
            rowIdB = Assert.Single(await dbB.GetAllStringsAsync(CancellationToken.None)).Id;
        }

        var (holdCode, _) = await RunCliAsync("hold", projectB.Key, "--id", rowIdB.ToString(System.Globalization.CultureInfo.InvariantCulture), "--note", "B에서 보류");
        Assert.Equal(0, holdCode);

        MovePlugin(pathB, pathA);
        var (reopenACode, reopenAOut) = await RunCliAsync("open", pathA);
        Assert.Equal(0, reopenACode);
        Assert.Contains(pathB, reopenAOut);

        var projectA = _session.Find(pathA);
        var held = await _session.HeldAsync(projectA, CancellationToken.None);
        var heldLine = Assert.Single(held);
        Assert.Equal("B에서 보류", heldLine.Reason);

        var (statusCode, statusOut) = await RunCliAsync("status", "Test.esp");
        Assert.Equal(0, statusCode);
        Assert.Contains($"프로젝트: {projectA.Key}", statusOut);
        Assert.Contains("보류", statusOut);
    }

    // R9: Agent commands other than open never create a missing project DB (both reads and writes fail cleanly).
    [Fact]
    public async Task R9_MissingDb_RefusesAllCommandsWithoutCreatingDb()
    {
        var v1 = WritePlugin("v1");
        var (project, _, _, _) = await _session.OpenAsync(v1, new PluginReadOptions(), "korean", "utf-8", batchSize: 10, CancellationToken.None);
        await _session.NextAsync(project, CancellationToken.None);

        long rowId;
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None))
        {
            rowId = Assert.Single(await db.GetAllStringsAsync(CancellationToken.None)).Id;
        }

        var answerFile = Path.Combine(_root, "answer.txt");
        await File.WriteAllTextAsync(answerFile, JsonSerializer.Serialize(new { translations = new[] { new { id = rowId, text = "철검" } } }), new UTF8Encoding(false));
        var editFile = Path.Combine(_root, "edit.json");
        await File.WriteAllTextAsync(editFile, JsonSerializer.Serialize(new[] { new { id = rowId, text = "강철검" } }), new UTF8Encoding(false));
        var contextFile = Path.Combine(_root, "context.txt");
        await File.WriteAllTextAsync(contextFile, "스카이림 시대 배경", new UTF8Encoding(false));
        var termsFile = Path.Combine(_root, "terms.json");
        await File.WriteAllTextAsync(termsFile, """[{"source":"Iron Sword","target":"철검"}]""", new UTF8Encoding(false));
        var koreanFile = WritePlugin("korean-release", "Test.esp", "철검");
        var exportDir = Path.Combine(_root, "export-out");

        ProjectDb.ReleasePool(project.DbPath);
        foreach (var path in new[] { project.DbPath, project.DbPath + "-wal", project.DbPath + "-shm" })
        {
            if (File.Exists(path)) File.Delete(path);
        }

        Assert.False(File.Exists(project.DbPath));

        var allCommands = new[]
        {
            // Write commands
            new[] { "next", project.Key },
            new[] { "submit", project.Key, answerFile },
            new[] { "review", project.Key, "--id", rowId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            new[] { "export", project.Key, exportDir },
            new[] { "edit", project.Key, editFile },
            new[] { "hold", project.Key, "--id", rowId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--note", "사유" },
            new[] { "names", project.Key, "--save" },
            new[] { "style", project.Key, "player", "반말", "--to", "other" },
            new[] { "context", project.Key, contextFile },
            new[] { "context", project.Key, "--clear" },
            new[] { "terms", project.Key, termsFile },
            new[] { "terms", project.Key, "Iron Sword", "--clear" },
            new[] { "korean", project.Key, koreanFile },
            // Read commands
            new[] { "status", project.Key },
            new[] { "check", project.Key },
            new[] { "show", project.Key },
            new[] { "lookup", project.Key, "Iron" },
            new[] { "names", project.Key },
            new[] { "voice", project.Key, "Lydia" },
            new[] { "style", project.Key },
            new[] { "context", project.Key },
            new[] { "terms", project.Key },
            new[] { "hold", project.Key },
        };

        foreach (var args in allCommands)
        {
            var (code, output) = await RunCliAsync(args);
            Assert.True(code != 0, $"Expected failure for '{string.Join(' ', args)}', got exit {code}:\n{output}");
            Assert.Contains("이 프로젝트의 DB가 없습니다", output);
            Assert.Contains(v1, output);
            Assert.False(File.Exists(project.DbPath), $"Command '{string.Join(' ', args)}' created an empty DB.");
        }

        // Lane subagent gets the extra instruction not to call open.
        var (laneCode, laneOut) = await RunCliAsync("status", project.Key, "--lane", "1/2");
        Assert.NotEqual(0, laneCode);
        Assert.Contains("이 프로젝트의 DB가 없습니다", laneOut);
        Assert.Contains("하위 에이전트는 open하지 말고", laneOut);
        Assert.False(File.Exists(project.DbPath));

        // When the plugin is missing too, both read and write give the missing-plugin guidance and still create no DB.
        File.Delete(v1);
        foreach (var args in new[] { new[] { "status", project.Key }, new[] { "next", project.Key } })
        {
            var (code, output) = await RunCliAsync(args);
            Assert.NotEqual(0, code);
            Assert.StartsWith("실패: 플러그인이 '", output);
            Assert.Contains(v1, output);
            Assert.DoesNotContain("이 프로젝트의 DB가 없습니다", output);
            Assert.False(File.Exists(project.DbPath));
        }
    }
}
