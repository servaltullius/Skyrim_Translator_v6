using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using XTranslatorAi.App.Services;
using XTranslatorAi.App.Services.AgentMode;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Tests.TestSupport;
using static XTranslatorAi.Tests.PluginReadWriteTests;

namespace XTranslatorAi.Tests;

/// <summary>
/// Agent mode: the GUI's translation run with the agent app's model writing the answers. A request covers every
/// remaining row (as the GUI's run does), a recording stops the run without splitting or leaving 번역 중 rows, and an
/// answer is used only where the run asks for it.
/// </summary>
public sealed class AgentModeTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agent-mode-tests", Guid.NewGuid().ToString("N"));
    private GlobalProjectDbService _global = null!;
    private AgentSession _session = null!;
    private AgentProject _project = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var plugin = Path.Combine(_root, "Mod", "Thalmor.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
        await File.WriteAllBytesAsync(plugin, Plugin());
        _global = new GlobalProjectDbService(new BuiltInGlossaryService(), Path.Combine(_root, "global"));
        _session = new AgentSession(Path.Combine(_root, "work"), _global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects"));
        (_project, _, _, _) = await _session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _global.DisposeAsync();
        TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(_project.DbPath);
        foreach (var db in _extraDbs) TestDbHelper.ReleaseProjectPoolAndDeleteDbFiles(db);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>tullius.cmd hands the arguments to cmd, which splits an unquoted "A&amp;B"; a file of arguments keeps them whole.</summary>
    [Fact]
    public async Task ArgumentFile_StandsForTheArgumentsInIt()
    {
        var file = Path.Combine(_root, "args.txt");
        await File.WriteAllLinesAsync(file, new[] { "open", @"G:\Mods\Fists & Fury ^1\Fists.esp", "", "  --batch-size  " });

        var args = AgentCli.ExpandArgumentFiles(new[] { "@" + file, "20" });

        Assert.Equal(new[] { "open", @"G:\Mods\Fists & Fury ^1\Fists.esp", "--batch-size", "20" }, args);
        Assert.Equal(new[] { "check", "@" }, AgentCli.ExpandArgumentFiles(new[] { "check", "@" }));
        Assert.Throws<AgentModeException>(() => AgentCli.ExpandArgumentFiles(new[] { "@" + file + ".missing" }));
    }

    /// <summary>
    /// An answer as each PowerShell writes it: 7 in UTF-8, 5.1 in UTF-16 (Out-File, &gt;) or CP949 (Set-Content on Korean
    /// Windows). Each reads back as written; text already broken is refused rather than saved.
    /// </summary>
    [Fact]
    public async Task AnswerFiles_AreReadInTheEncodingTheAppWroteThem()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        const string answer = "{\"translations\":[{\"id\":1,\"text\":\"파린스, 같이 가자 & 끝.\"}]}";
        var encodings = new (string Name, System.Text.Encoding Encoding)[]
        {
            ("utf8", new System.Text.UTF8Encoding(false)), ("utf8bom", new System.Text.UTF8Encoding(true)),
            ("utf16", System.Text.Encoding.Unicode), ("cp949", System.Text.Encoding.GetEncoding(949)),
        };
        foreach (var (name, encoding) in encodings)
        {
            var path = Path.Combine(_root, $"answer-{name}.txt");
            await File.WriteAllTextAsync(path, answer, encoding);
            Assert.Equal(answer, await AgentCli.ReadAgentFileAsync(path, CancellationToken.None));
        }

        var args = Path.Combine(_root, "args-cp949.txt");
        await File.WriteAllTextAsync(args, "lookup\r\nMeridia\r\n검은 & 손\r\n", System.Text.Encoding.GetEncoding(949));
        Assert.Equal(new[] { "lookup", "Meridia", "검은 & 손" }, AgentCli.ExpandArgumentFiles(new[] { "@" + args }));

        var broken = Path.Combine(_root, "answer-broken.txt");
        await File.WriteAllTextAsync(broken, "\uFFFD\uFFFD 깨진 답", new System.Text.UTF8Encoding(false));
        await Assert.ThrowsAsync<AgentModeException>(() => AgentCli.ReadAgentFileAsync(broken, CancellationToken.None));
    }

    /// <summary>
    /// The Codex trial: a level decided with a request waiting dropped that request's answer. The request is made again
    /// at once, and request.md names the instructions file by its full path (--work moved it from the relative work).
    /// </summary>
    [Fact]
    public async Task WaitingRequest_IsMadeAgain_AndNamesItsInstructionsByFullPath()
    {
        Assert.Null((await _session.RemakePendingAsync(_project, CancellationToken.None)).Report);
        await _session.NextAsync(_project, CancellationToken.None);

        var (remade, lanes, left) = await _session.RemakePendingAsync(_project, CancellationToken.None);

        Assert.NotNull(remade?.Next);
        Assert.Equal(0, lanes);
        Assert.False(left);
        Assert.NotNull(_session.Pending(_project));
        Assert.Contains(_session.InstructionsPath(_project), await File.ReadAllTextAsync(_session.RequestPath(_project)));
    }

    /// <summary>A PowerShell function passes "--id 1008,383" as separate arguments; the list is gathered whole.</summary>
    [Fact]
    public void IdList_SplitIntoArguments_IsGatheredWhole()
    {
        var command = AgentCommand.Parse(new List<string> { "proj", "--id", "1008", "383,275", "--work", Path.Combine(_root, "w"), "extra" });

        Assert.Equal("1008,383,275", command.Ids);
        Assert.Equal(new[] { "proj", "extra" }, command.Rest);
    }

    /// <summary>
    /// --global puts the global DBs, and the bundled TM written for them, in another folder: a comparison over project
    /// copies, and these tests, opened the user's own global DB, which an open migrates and tops up.
    /// </summary>
    [Fact]
    public async Task GlobalOption_KeepsTheGlobalDbsInItsFolder()
    {
        var global = Path.Combine(_root, "global-option");
        var command = AgentCommand.Parse(new List<string> { "--global", global, "--work", Path.Combine(_root, "work") });
        Assert.Equal(Path.GetFullPath(global), command.GlobalRoot);
        Assert.Equal(Path.GetFullPath(global), command.GlobalDb.GlobalRootOverride);
        await command.GlobalDb.DisposeAsync();

        var code = await AgentCli.RunAsync(new[] { "next", _project.Key, "--work", Path.Combine(_root, "work"),
            "--projects", Path.Combine(_root, "projects"), "--global", global }, argumentFiles: false);

        Assert.Equal(0, code);
        Assert.NotEmpty(Directory.GetFiles(global, "*.sqlite", SearchOption.AllDirectories));
        // The bundled TM written for that global DB, not into the user's tm-import.
        Assert.NotEmpty(Directory.GetFiles(global, ".bundled-seed.elderscrolls.*.stamp"));

        // The MCP server hands it to every tool call: a lookup opens the global DB in that folder.
        var viaMcp = Path.Combine(_root, "global-mcp");
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", viaMcp });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "lookup", arguments = new { project = "Thalmor", words = new[] { "Dominion" } } },
        });
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), new StringWriter()));
        Assert.NotEmpty(Directory.GetFiles(viaMcp, "*.sqlite", SearchOption.AllDirectories));

        // Its folder left out, or an option in its place, is refused: the command went on with the user's own.
        foreach (var bad in new[] { new[] { "--global" }, new[] { "--global", "--projects", "P" }, new[] { "--global", "" } })
        {
            Assert.Throws<AgentModeException>(() => AgentCommand.Parse(bad.ToList()));
        }
    }

    /// <summary>
    /// The MCP server: one JSON-RPC reply per request line and none for a notification, a version the client asked for
    /// when it is known, and errors as replies rather than a stopped server. The request tool reads the waiting request.
    /// </summary>
    [Fact]
    public async Task McpServer_AnswersEachRequest_AndItsRequestToolReadsTheWaitingRequest()
    {
        await _session.NextAsync(_project, CancellationToken.None);
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        var lines = new[]
        {
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            "{not json",
            """{"jsonrpc":"2.0","id":3,"method":"prompts/list"}""",
            """{"jsonrpc":"2.0","id":"r4","method":"tools/call","params":{"name":"nosuch","arguments":{}}}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"request","arguments":{"project":"Thalmor"}}}""",
            // A method that is not a string ended the server; an unknown project came back as a protocol error.
            """{"jsonrpc":"2.0","id":7,"method":42}""",
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"request","arguments":{"project":"NoSuchProject"}}}""",
        };
        var output = new StringWriter();

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(string.Join("\n", lines)), output));

        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => JsonDocument.Parse(r).RootElement).ToList();
        Assert.Equal(8, replies.Count);
        Assert.Equal(-32600, replies[6].GetProperty("error").GetProperty("code").GetInt32());
        Assert.True(replies[7].GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("2025-03-26", replies[0].GetProperty("result").GetProperty("protocolVersion").GetString());
        var tools = replies[1].GetProperty("result").GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!);
        Assert.Contains("submit", tools.Keys);
        Assert.Equal(new[] { "project", "answer" }, tools["submit"].GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(-32700, replies[2].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32601, replies[3].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("r4", replies[4].GetProperty("id").GetString());
        Assert.True(replies[4].GetProperty("result").GetProperty("isError").GetBoolean());
        var request = replies[5].GetProperty("result");
        Assert.False(request.GetProperty("isError").GetBoolean());
        Assert.Equal(await File.ReadAllTextAsync(_session.RequestPath(_project)), request.GetProperty("content")[0].GetProperty("text").GetString());

        // The answer goes through a file of its own, gone after the call: lanes' servers share the work folder, and a
        // fixed answer.txt let one lane's answer be saved into another lane's request. The call goes to a project that
        // does not exist: a real submit would open the user's global DB (the server's commands have no test root).
        var submit = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 6, method = "tools/call",
            @params = new { name = "submit", arguments = new { project = "NoSuchProject", answer = "번역" } },
        });
        var submitted = new StringWriter();
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(submit), submitted));
        Assert.True(JsonDocument.Parse(submitted.ToString()).RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "work", ".mcp")));
    }

    // A style kept for an NPC whose master left is named "@Dawnguard.esm|01001234". Through MCP the name went through
    // the "@file" arguments and failed as a missing file, so the guide's way to clear it could not work.
    [Fact]
    public async Task McpStyle_ClearsAStyleKeptForAMasterThatLeft_AndSaysWhenThereIsNothingToClear()
    {
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.SetSpeakerStyleAsync(new SpeakerStyle("@Dawnguard.esm|01001234", "", "반말"), CancellationToken.None);
        }

        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        string Call(int id, object arguments) => JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id, method = "tools/call", @params = new { name = "style", arguments },
        });
        var output = new StringWriter();
        // The first as the list shows it (the guide says to copy it as listed), the note after it included.
        var lines = string.Join("\n",
            Call(1, new { project = "Thalmor", speaker = "@Dawnguard.esm|01001234 (마스터가 빠져 쓰이지 않음, 돌아오면 다시 쓰임)", clear = true }),
            Call(2, new { project = "Thalmor", speaker = "@Dawnguard.esm|01001234", clear = true }));

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(lines), output));

        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => JsonDocument.Parse(r).RootElement.GetProperty("result")).ToList();
        Assert.False(replies[0].GetProperty("isError").GetBoolean(), replies[0].ToString());
        Assert.StartsWith("지움:", replies[0].GetProperty("content")[0].GetProperty("text").GetString());
        // Clearing it again printed 지움 too, though nothing was cleared.
        Assert.True(replies[1].GetProperty("isError").GetBoolean());
        Assert.Contains("지울 말투가 없습니다", replies[1].GetProperty("content")[0].GetProperty("text").GetString());
        await using var check = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        Assert.Empty(await check.GetSpeakerStylesAsync(CancellationToken.None));
    }

    // An older Korean release saved in CP949 could not be linked (it is read in the UTF-8 output encoding); it is read
    // as CP949 now. A release that cannot be read at all ended the CLI with .NET's "Unhandled exception" and an MCP call
    // with a protocol error; the agent looks for "실패:".
    [Fact]
    public async Task McpKorean_LinksACp949KoreanRelease_AndReportsAnUnreadableOneAsAFailure()
    {
        var korean = Path.Combine(_root, "Korean", "Thalmor.esp");
        var broken = Path.Combine(_root, "Korean", "Broken.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        await File.WriteAllBytesAsync(korean, Header().Concat(Group(Record("MISC", 0x2001,
            Sub("FULL", PluginBinary.GetEncoding("ks_c_5601-1987").GetBytes("에테르의 메아리\0"))))).ToArray());
        await File.WriteAllBytesAsync(broken, Header()[..20]);
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        string Call(int id, string file) => JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id, method = "tools/call",
            @params = new { name = "korean", arguments = new { project = "Thalmor", korean_plugin = file } },
        });
        var output = new StringWriter();

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(Call(1, broken) + "\n" + Call(2, korean)), output));

        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => JsonDocument.Parse(r).RootElement).ToList();
        Assert.All(replies, reply => Assert.False(reply.TryGetProperty("error", out _), reply.ToString()));
        Assert.True(replies[0].GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.StartsWith("실패: ", replies[0].GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
        var linked = replies[1].GetProperty("result");
        Assert.False(linked.GetProperty("isError").GetBoolean(), linked.ToString());
        Assert.Contains("CP949로 읽었습니다", linked.GetProperty("content")[0].GetProperty("text").GetString());
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        Assert.Contains("에테르의 메아리", (await db.GetPreviousTranslationsByStringIdAsync(CancellationToken.None)).Values);
    }

    // The kit alone never imported the bundled series TM (only the GUI did, when it opened a project), so agent mode
    // translated with an empty TM: no TM fills, no official names in requests, lookup or check, and no warning.
    [Fact]
    public async Task Open_ImportsTheBundledSeriesTm_AsTheGuiDoes_AndOnlyOnce()
    {
        var root = Path.Combine(_root, "seed");
        await using var global = new GlobalProjectDbService(new BuiltInGlossaryService(), root);
        var seed = new BundledFranchiseTmSeedService(root);
        var plugin = Path.Combine(_root, "Mod", "Thalmor.esp");
        var session = new AgentSession(Path.Combine(_root, "work-seed"), global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-seed"), tmSeed: seed);

        var (first, _, _, _) = await session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
        _extraDbs.Add(first.DbPath);
        var db = await global.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
        var imported = (await db!.GetTranslationMemoryEntriesAsync("english", "korean", CancellationToken.None)).Count;

        Assert.True(imported > 20000, $"series TM rows: {imported}");
        Assert.Contains("가져왔습니다", session.SeriesTmNote);
        await session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
        Assert.Null(session.SeriesTmNote);
        Assert.Equal(imported, (await db.GetTranslationMemoryEntriesAsync("english", "korean", CancellationToken.None)).Count);
    }

    // A global DB that cannot be opened at all (damaged, locked) went unreported: the GUI asks before a run, while the
    // agent's step saved its rows without the global glossary, series TM and name index, and said nothing.
    [Fact]
    public async Task UnopenableGlobalDb_IsInTheStepReport()
    {
        var root = Path.Combine(_root, "broken-global");
        var path = ProjectPaths.GetGlobalGlossaryDbPath(BethesdaFranchise.ElderScrolls, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes(new string('x', 4096)));
        await using var global = new GlobalProjectDbService(new BuiltInGlossaryService(), root);
        var session = new AgentSession(Path.Combine(_root, "work"), global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects"));

        var report = await session.NextAsync(_project, CancellationToken.None);

        Assert.Contains("시리즈 TM", report.MissingGlobalData);
        Assert.Contains("전체 용어집", report.MissingGlobalData);
    }

    // A run without a lane covers every lane's rows: with a lane's request waiting it asked the same lines again and the
    // lane's answer was dropped. A new conversation that saw "대기 중인 요청: 없음" in status went that way.
    [Fact]
    public async Task LaneRequestWaiting_RefusesTheRunWithoutALane()
    {
        await _session.NextAsync(_project with { Lane = AgentLane.Parse("1/2") }, CancellationToken.None);

        var refused = await Assert.ThrowsAsync<AgentModeException>(() => _session.NextAsync(_project, CancellationToken.None));

        Assert.Contains("갈래 1/2", refused.Message);
        Assert.Equal(new[] { "1/2" }, _session.LanesWaiting(_project));
        await Assert.ThrowsAsync<AgentModeException>(() => _session.SubmitAsync(_project, "번역", CancellationToken.None));
    }

    // The other way round: a lane's first request, made while the laneless one waited, asked that request's lines again.
    [Fact]
    public async Task LanelessRequestWaiting_RefusesALanesFirstRequest()
    {
        await _session.NextAsync(_project, CancellationToken.None);

        var refused = await Assert.ThrowsAsync<AgentModeException>(
            () => _session.NextAsync(_project with { Lane = AgentLane.Parse("1/2") }, CancellationToken.None));

        Assert.Contains("갈래 없이 만든 요청", refused.Message);
        Assert.Empty(_session.LanesWaiting(_project));
    }

    // Both waiting (made by an earlier version): a decided level remade the laneless request by clearing it and calling
    // next, which refused, so the request was gone and style ended in 실패.
    [Fact]
    public async Task RemakingARequest_ThatNextWouldRefuse_LeavesIt()
    {
        await _session.NextAsync(_project, CancellationToken.None);
        var laneDir = Path.Combine(_root, "work", _project.Key, AgentLane.Parse("1/2")!.Folder);
        Directory.CreateDirectory(laneDir);
        File.Copy(Path.Combine(_root, "work", _project.Key, "pending.json"), Path.Combine(laneDir, "pending.json"));

        var (report, lanes, left) = await _session.RemakePendingAsync(_project, CancellationToken.None);

        Assert.Null(report);
        Assert.Equal(1, lanes);
        Assert.True(left);
        Assert.NotNull(_session.Pending(_project));
    }

    // Both waiting (made by an earlier version): a lane's submit saved its answer and made its next request, which asked
    // the laneless request's lines again.
    [Fact]
    public async Task LaneSubmit_WithTheLanelessRequestWaiting_SavesButMakesNoNextRequest()
    {
        var lane = _project with { Lane = AgentLane.Parse("1/2") };
        await _session.NextAsync(lane, CancellationToken.None);
        var pending = _session.Pending(lane)!;
        File.Copy(Path.Combine(_root, "work", _project.Key, lane.Lane!.Folder, "pending.json"), Path.Combine(_root, "work", _project.Key, "pending.json"));
        var answer = JsonSerializer.Serialize(new
        {
            translations = AgentAnswerCheck.Lines(pending).Where(line => line.Id != null).Select(line => new { id = line.Id, text = "번역문입니다." }),
        });

        var report = await _session.SubmitAsync(lane, answer, CancellationToken.None);

        Assert.Null(report.Next);
        Assert.Contains("다음 요청은 만들지 않았습니다", report.Problem);
        Assert.Null(_session.Pending(lane));
        Assert.NotNull(_session.Pending(_project));
    }

    // A file another program holds open for reading is imported but cannot be moved: the move's error stopped the loop,
    // and the bundled seed after it in name order was never imported.
    [Fact]
    public async Task SeriesTmImport_WithAFileThatCannotBeMoved_TakesTheRest()
    {
        var root = Path.Combine(_root, "seed-held");
        var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
        Directory.CreateDirectory(importDir);
        var held = Path.Combine(importDir, "aaa.tsv");
        await File.WriteAllTextAsync(held, "Source\tTarget\nHello\t안녕\n");
        await using var global = new GlobalProjectDbService(new BuiltInGlossaryService(), root);
        var session = new AgentSession(Path.Combine(_root, "work-held"), global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-held"), tmSeed: new BundledFranchiseTmSeedService(root));

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var (project, _, _, _) = await session.OpenAsync(Path.Combine(_root, "Mod", "Thalmor.esp"), new PluginReadOptions(), "korean", "utf-8",
                batchSize: 2, CancellationToken.None);
            _extraDbs.Add(project.DbPath);
        }

        var db = await global.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
        Assert.True((await db!.GetTranslationMemoryEntriesAsync("english", "korean", CancellationToken.None)).Count > 20000);
        Assert.Contains("옮기지 못했습니다", session.SeriesTmNote);
    }

    // A user's TSV open in Excel (locked) stopped the import loop, and the bundled seed after it in name order was never
    // imported; agent mode said nothing.
    [Fact]
    public async Task SeriesTmImport_WithALockedFile_TakesTheRest_AndSaysWhich()
    {
        var root = Path.Combine(_root, "seed-locked");
        var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(BethesdaFranchise.ElderScrolls, root);
        Directory.CreateDirectory(importDir);
        var locked = Path.Combine(importDir, "aaa.tsv");
        await File.WriteAllTextAsync(locked, "Source\tTarget\nHello\t안녕\n");
        await using var global = new GlobalProjectDbService(new BuiltInGlossaryService(), root);
        var session = new AgentSession(Path.Combine(_root, "work-locked"), global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-locked"), tmSeed: new BundledFranchiseTmSeedService(root));

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (project, _, _, _) = await session.OpenAsync(Path.Combine(_root, "Mod", "Thalmor.esp"), new PluginReadOptions(), "korean", "utf-8",
                batchSize: 2, CancellationToken.None);
            _extraDbs.Add(project.DbPath);
        }

        var db = await global.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
        Assert.True((await db!.GetTranslationMemoryEntriesAsync("english", "korean", CancellationToken.None)).Count > 20000);
        Assert.Contains("aaa.tsv", session.SeriesTmNote);
        Assert.True(File.Exists(locked));
    }

    // A one-line request has no ids to check: the last request's answer given again (answer.txt not rewritten) was saved
    // into this line.
    [Fact]
    public async Task TextRequest_AnsweredWithAnEarlierAnswer_IsRefused()
    {
        var session = new AgentSession(Path.Combine(_root, "work-text"), _global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-text"));
        var (project, _, _, _) = await session.OpenAsync(Path.Combine(_root, "Mod", "Thalmor.esp"), new PluginReadOptions(), "korean", "utf-8",
            batchSize: 1, CancellationToken.None);
        _extraDbs.Add(project.DbPath);
        async Task<AgentRequest> NextTextAsync(AgentStepReport report)
        {
            while (report.Next is { } json && IsJson(json))
            {
                report = await session.SubmitAsync(project, Answer(json), CancellationToken.None);
            }

            return report.Next!;
        }

        var first = await NextTextAsync(await session.NextAsync(project, CancellationToken.None));
        var second = await NextTextAsync(await session.SubmitAsync(project, Answer(first), CancellationToken.None));

        Assert.NotEqual(first.Hash, second.Hash);
        var refused = await Assert.ThrowsAsync<AgentModeException>(() => session.SubmitAsync(project, Answer(first), CancellationToken.None));
        Assert.Contains("앞 요청에 낸 답과 같아", refused.Message);
        Assert.Equal(second.Hash, session.Pending(project)!.Hash);
    }

    // The earlier English was read with the source encoding now in the settings: changed for another file since open, a
    // UTF-8 "Dragon’s Bane" read as Korean and was refused.
    [Fact]
    public async Task KoreanWithEnglish_ReadsTheEnglishAsTheProjectWasOpened()
    {
        static byte[] Bane(string name) => Header().Concat(Group(Record("MISC", 0x2101, Sub("EDID", Z("aaBane")), Sub("FULL", Z(name))))).ToArray();
        var session = new AgentSession(Path.Combine(_root, "work-bane"), _global, gameStrings: null,
            new AppSettings() with { PluginSourceEncoding = "ks_c_5601-1987" }, projectsRootOverride: Path.Combine(_root, "projects-bane"));
        var current = Path.Combine(_root, "Mod", "Bane.esp");
        var korean = Path.Combine(_root, "Korean", "Bane.esp");
        var english = Path.Combine(_root, "English", "Bane.esp");
        foreach (var (path, name) in new[] { (current, "Dragon’s Bane"), (korean, "용의 재앙"), (english, "Dragon’s Bane") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, Bane(name));
        }

        var (project, _, _, _) = await session.OpenAsync(current, new PluginReadOptions(SourceEncoding: "utf-8"), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);
        var link = await session.LinkKoreanAsync(session.Find(project.Key), korean, null, CancellationToken.None, english);

        Assert.True(link.ComparedWithEnglish);
        Assert.Equal(1, link.Filled);
    }

    // Remaking the waiting request said nothing when there was no new request (a run that failed, or nothing left).
    [Fact]
    public void RemakingAWaitingRequest_SaysWhenThereIsNone()
    {
        Assert.Null(AgentCli.DescribeRemade(null, "정한 말투로"));
        var failed = new AgentStepReport(_project, 3, 3, 0, null, null, "오류: 잠김") { MissingGlobalData = new[] { "시리즈 TM" } };
        var note = AgentCli.DescribeRemade(failed, "정한 말투로")!;
        Assert.Contains("다시 만들지 못했습니다: 오류: 잠김", note);
        Assert.Contains("경고: 전체 DB의 시리즈 TM", note);
        Assert.Contains("모두 번역돼", AgentCli.DescribeRemade(new AgentStepReport(_project, 3, 0, 0, null, null, null), "정한 말투로"));
        // A line left in error is not "모두 번역돼".
        Assert.Contains("남은 줄 1, 오류 1", AgentCli.DescribeRemade(new AgentStepReport(_project, 3, 1, 1, null, null, null), "정한 말투로"));
    }

    // The same English asked in two requests (two topics, a repair of the line) takes the same answer: compared with the
    // other answers alone, it was refused as the last answer given again.
    [Fact]
    public async Task TextRequests_OfTheSameEnglish_TakeTheSameAnswer()
    {
        var session = new AgentSession(Path.Combine(_root, "work-twice"), _global, gameStrings: null, new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-twice"));
        var plugin = Path.Combine(_root, "Mod", "Twice.esp");
        await File.WriteAllBytesAsync(plugin, Header().Concat(DialogueGroup(
                TopicChildren(0x1000, Record("INFO", 0x1001, Sub("NAM1", Z("Speak with care, traveler."))),
                    Record("INFO", 0x1002, Sub("NAM1", Z("The road north is closed.")))),
                TopicChildren(0x1100, Record("INFO", 0x1101, Sub("NAM1", Z("Speak with care, traveler."))),
                    Record("INFO", 0x1102, Sub("NAM1", Z("Watch the skies tonight."))))))
            .ToArray());
        var (project, _, _, _) = await session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 1, CancellationToken.None);
        _extraDbs.Add(project.DbPath);

        var report = await session.NextAsync(project, CancellationToken.None);
        for (var i = 0; i < 20 && report.Next is { } request; i++)
        {
            report = await session.SubmitAsync(project, Answer(request), CancellationToken.None);
        }

        Assert.Null(report.Next);
        Assert.Equal(0, report.PendingAfter);
    }

    // A lane's agent that left out lane was told "next로 요청을 만드세요" and made a laneless request over the lanes' rows.
    [Fact]
    public async Task McpRequest_WithoutTheLane_SaysWhichLaneWaits()
    {
        await _session.NextAsync(_project with { Lane = AgentLane.Parse("1/2") }, CancellationToken.None);
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "request", arguments = new { project = "Thalmor" } },
        });
        var output = new StringWriter();

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), output));

        var text = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("갈래 1/2에 대기 중인 요청", text);
    }

    // "korean <키> --english <영어판> <한국어판>" read --english as the release's path: the options came out after it.
    [Fact]
    public async Task KoreanCommand_TakesItsOptionsWhereverTheyAre()
    {
        var korean = Path.Combine(_root, "Korean-order", "Thalmor.esp");
        var english = Path.Combine(_root, "English-order", "Thalmor.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        Directory.CreateDirectory(Path.GetDirectoryName(english)!);
        await File.WriteAllBytesAsync(korean, Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("도미니언에 대한 충성은 기록해 두었다."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("그럼 서로 이해한 걸로 하지."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("말조심해라, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("에테르의 메아리")))))
            .ToArray());
        await File.WriteAllBytesAsync(english, Plugin());

        var code = await AgentCli.RunAsync(new[] { "korean", _project.Key, "--english", english, korean,
            "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") }, argumentFiles: false);

        Assert.Equal(0, code);
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        Assert.NotNull(await db.GetPreviousTranslationSourceAsync(CancellationToken.None));
        Assert.Equal("Thalmor.esp", await db.GetPreviousSourceFileNameAsync(CancellationToken.None));
    }

    // A run that failed during submit (a locked DB, a full disk) said "다시 만들지 않아 … 새로 만든 요청에 답하세요",
    // hiding the error, and deleted the request and the answer with no new request written.
    [Fact]
    public async Task SubmitWhoseRunFails_KeepsTheRequestAndTheAnswer_AndSaysWhy()
    {
        var first = await _session.NextAsync(_project, CancellationToken.None);
        var pending = _session.Pending(_project)!;
        var answer = JsonSerializer.Serialize(new
        {
            translations = AgentAnswerCheck.Lines(pending).Where(line => line.Id != null).Select(line => new { id = line.Id, text = "번역문입니다." }),
        });
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_project.DbPath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "CREATE TRIGGER review4_block BEFORE UPDATE ON StringEntry BEGIN SELECT RAISE(ABORT, 'review4 boom'); END;";
            await trigger.ExecuteNonQueryAsync();
        }

        var report = await _session.SubmitAsync(_project, answer, CancellationToken.None);

        Assert.True(report.RunFailed);
        Assert.Contains("review4 boom", report.Problem);
        Assert.Contains("그대로 두었습니다", report.Problem);
        Assert.NotNull(_session.Pending(_project));
        Assert.Equal(first.Next!.Hash, _session.Pending(_project)!.Hash);
    }

    // A translation the writer refuses (a tag lost, often in a line a Korean release filled) ended export with .NET's
    // "Unhandled exception" (an MCP protocol error), naming a field key the agent cannot edit.
    [Fact]
    public async Task McpExport_WithATagLost_FailsNamingTheRow()
    {
        long id, other;
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            var row = rows.Single(r => r.SourceText.Contains("<Alias=Player>", StringComparison.Ordinal));
            id = row.Id;
            await db.UpdateStringTranslationAsync(id, "말조심해라.", StringEntryStatus.Edited, null, CancellationToken.None);
            // A second line refused: the writer stops at the first, and the agent fixed one per export.
            other = rows.Single(r => r.SourceText.StartsWith("Your loyalty", StringComparison.Ordinal)).Id;
            await db.UpdateStringTranslationAsync(other, "<Alias=Player>, 네 충성은 기록되었다.", StringEntryStatus.Edited, null, CancellationToken.None);
        }

        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "export", arguments = new { project = "Thalmor" } },
        });
        var output = new StringWriter();

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), output));

        var reply = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.False(reply.TryGetProperty("error", out _), output.ToString());
        var text = reply.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.StartsWith("실패: ", text);
        Assert.Contains("#" + id, text);
        Assert.Contains("#" + other, text);
        Assert.Contains("첫 줄의 사유", text);
    }

    private async Task<string> McpTextAsync(string tool, object arguments)
    {
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        var call = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments } });
        var output = new StringWriter();
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), output));
        return JsonDocument.Parse(output.ToString()).RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
    }

    // The export said only where it saved: a run stopped halfway, and the lines held, went out in English without a word.
    [Fact]
    public async Task Export_SaysTheLinesItWroteInEnglish_AsTheGuiAsks()
    {
        long held;
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows.Single(r => r.SourceText.StartsWith("Your loyalty", StringComparison.Ordinal)).Id,
                "도미니언에 대한 네 충성은 기록되었다.", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows.Single(r => r.SourceText.StartsWith("Then we", StringComparison.Ordinal)).Id,
                "", StringEntryStatus.Error, "boom", CancellationToken.None);
            held = rows.Single(r => r.SourceText == "Aetherial Echo").Id;
        }

        await _session.HoldAsync(_project, new[] { held }, "시험", CancellationToken.None);

        var text = await McpTextAsync("export", new { project = "Thalmor" });

        Assert.Contains("저장: ", text);
        Assert.Contains("알림: 번역이 끝나지 않은 줄 2(오류 1, 대기 1)은 영어 그대로 저장했습니다", text);
        Assert.Contains("알림: 보류한 줄 1은 영어 그대로 저장했습니다", text);
    }

    // The writer names a field by its key, by its record or by its 순번, by which the agent finds no row (nor the GUI's
    // user). A key at a sentence's end took its period ("…/NAM1/0."); a record named every row of it; "관련 행: #1" is
    // the 순번, not the id the agent edits by.
    [Fact]
    public async Task ExportErrors_NameTheRows_ByFieldKeyRecordOrOrder()
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var document = await PluginReader.ReadAsync(_project.PluginPath, _project.ReadOptions, CancellationToken.None);
        var keys = await db.GetPluginFieldKeysByStringIdAsync(CancellationToken.None);
        var line = keys.Single(pair => pair.Value.StartsWith("INFO/00001003/", StringComparison.Ordinal));
        var name = keys.Single(pair => pair.Value.StartsWith("MISC/", StringComparison.Ordinal));
        Task<IReadOnlyList<long>> Rows(string message) => PluginExportErrors.RowIdsAsync(db, document, message, CancellationToken.None);

        Assert.Equal(new[] { line.Key }, await Rows($"Token count mismatch (plugin:{line.Value}): 1 != 0"));
        Assert.Equal(new[] { line.Key }, await Rows($"Protected formatting order mismatch for plugin:{line.Value}."));

        // Of the record's rows, the one whose written translation is the refusal's.
        const string empty = "비어 있는 번역을 저장할 수 없습니다: MISC:FULL/00002001";
        const string unencodable = "출력 인코딩으로 표현할 수 없는 문자가 있습니다: MISC:FULL/00002001 U+AC00";
        Assert.Empty(await Rows(empty));
        Assert.Empty(await Rows(unencodable));
        await db.UpdateStringTranslationAsync(name.Key, "", StringEntryStatus.Edited, null, CancellationToken.None);
        Assert.Equal(new[] { name.Key }, await Rows(empty));
        await db.UpdateStringTranslationAsync(name.Key, "가상의 메아리", StringEntryStatus.Edited, null, CancellationToken.None);
        Assert.Equal(new[] { name.Key }, await Rows(unencodable));
        Assert.Empty(await Rows(empty));

        var byOrder = (await db.GetStringsAsync(50, 0, CancellationToken.None)).ToDictionary(r => r.OrderIndex, r => r.Id);
        const string shared = "여러 필드가 같은 Strings StringID 5를 공유하지만 번역이 다릅니다. Search에 string:STRINGS/5 입력 후 Status를 All로 설정하고 "
                              + "공유 행의 번역을 일치시켜 주세요. 관련 행: #1, #2";
        Assert.Equal(new[] { byOrder[1], byOrder[2] }, await Rows(shared));
        Assert.DoesNotContain("관련 행", PluginExportErrors.Reason(shared));
        Assert.Contains("Strings StringID 5", PluginExportErrors.Reason(shared));

        Assert.Empty(await Rows("비번역 데이터가 변경되었습니다: INFO:CTDA/00001001"));
        Assert.Empty(await Rows("불러온 뒤 원본이 변경되었습니다. 다시 열어주세요: Thalmor.esp"));
    }

    // A diagnostic given for each of many fields filled the open's output; one that stops the export is a warning.
    [Fact]
    public void OpenNotes_AreCutShortByKind_AndAnExportStopIsAWarning()
    {
        var notes = new List<PluginOpenNotes.Note> { new("번역 2개를 보관해 두었습니다"), new("지원 정의에 없는 레코드 ZZZZ가 있습니다.", true, "unknown_record") };
        notes.AddRange(Enumerable.Range(1, 5).Select(n => new PluginOpenNotes.Note($"QUST:NNAM/0000100{n}는 도구별 필드 정의가 달라 직접 번역하지 않습니다.", false, "ambiguous_field")));

        var lines = AgentCli.OpenNoteLines(notes).ToList();

        Assert.Equal("알림: 번역 2개를 보관해 두었습니다", lines[0]);
        Assert.StartsWith("경고: 이 플러그인은 번역해도 export할 수 없습니다: 지원 정의에 없는 레코드 ZZZZ", lines[1]);
        Assert.Equal(3, lines.Count(l => l.Contains("QUST:NNAM", StringComparison.Ordinal)));
        Assert.Equal("알림: 같은 종류의 알림 2개를 더 생략했습니다(ambiguous_field).", lines[^1]);
    }

    // The agent's open said only the glossary it inherited and the encoding; the GUI's status line says what the open
    // found. A line a stopped run left 번역 중 is 대기 again after the open, and counted.
    [Fact]
    public async Task Reopen_SaysWhatTheGuiSays_AndCountsALineAStoppedRunLeft()
    {
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows.Single(r => r.SourceText.StartsWith("Then we", StringComparison.Ordinal)).Id,
                "그럼 이야기가 된 거군.", StringEntryStatus.Done, null, CancellationToken.None);
            await db.UpdateStringTranslationAsync(rows.Single(r => r.SourceText == "Aetherial Echo").Id, "", StringEntryStatus.InProgress, null, CancellationToken.None);
        }

        // The mod updated: the line translated reads otherwise now.
        await File.WriteAllBytesAsync(_project.PluginPath, Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("Your loyalty to the Dominion has been noted."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("Then we have a deal."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("Aetherial Echo")))))
            .ToArray());

        var (project, _, pending, _) = await _session.OpenAsync(_project.PluginPath, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);

        Assert.Contains(_session.OpenNotes, note => note.Text.Contains("번역 1개를 보관해 두었습니다", StringComparison.Ordinal));
        Assert.Equal(4, pending);
        Assert.Equal(4, (await _session.StatusAsync(project, CancellationToken.None)).Pending);
    }

    // Codex ran in a kit folder that was not named agent-kit, its shell failed, and the guide tool answered
    // "없는 안내: translate" for a name that exists, so it stopped. The server now also reads the kit it is started in,
    // and when there is none says where it looked.
    [Fact]
    public async Task McpServer_GuideReadsTheRenamedKitItRunsIn_AndSaysWhereItLookedOtherwise()
    {
        var exeFolder = Path.Combine(_root, "translator");
        var kit = Path.Combine(exeFolder, "kit-codex");
        Directory.CreateDirectory(kit);
        var beside = Path.Combine(exeFolder, "agent-kit");
        // Another project's AGENTS.md is not a kit: Antigravity starts the global server in whatever folder is open.
        await File.WriteAllTextAsync(Path.Combine(kit, "AGENTS.md"), "rules");
        Assert.Equal(beside, AgentMcpServer.KitFolder(exeFolder, kit));
        await File.WriteAllTextAsync(Path.Combine(kit, "tullius.cmd"), "");
        Assert.Equal(kit, AgentMcpServer.KitFolder(exeFolder, kit));
        Directory.CreateDirectory(beside);
        Assert.Equal(beside, AgentMcpServer.KitFolder(exeFolder, kit));

        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        var call = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "guide", arguments = new { name = "translate" } },
        });
        var output = new StringWriter();
        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), output));
        var result = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains(Path.Combine("agent-kit", "guide-translate.md"), text);
        Assert.Contains("TulliusTranslator.exe 바로 옆", text);
    }

    // Antigravity reads one global MCP config; pasting the mcp-config text into it was its one manual setup step.
    // The install keeps the other servers, copies a file with content aside, and leaves a file that is not JSON alone.
    [Fact]
    public void AntigravityInstall_AddsTulliusBesideTheOtherServers()
    {
        var config = Path.Combine(_root, "gemini", "config", "mcp_config.json");
        var first = AgentMcpServer.InstallInto(config, @"C:\Tullius\TulliusTranslator.exe");
        Assert.True(first.Changed);
        Assert.Null(first.Backup);
        var written = JsonDocument.Parse(File.ReadAllText(config)).RootElement.GetProperty("mcpServers").GetProperty("tullius");
        Assert.Equal(@"C:\Tullius\TulliusTranslator.exe", written.GetProperty("command").GetString());
        Assert.Equal(new[] { "agent", "mcp" }, written.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.False(AgentMcpServer.InstallInto(config, @"C:\Tullius\TulliusTranslator.exe").Changed);

        File.WriteAllText(config, """{"mcpServers":{"other":{"command":"x.exe"}},"theme":"dark"}""");
        var moved = AgentMcpServer.InstallInto(config, @"D:\Moved\TulliusTranslator.exe");
        Assert.True(moved.Changed);
        Assert.True(File.Exists(moved.Backup));
        var root = JsonDocument.Parse(File.ReadAllText(config)).RootElement;
        Assert.Equal("x.exe", root.GetProperty("mcpServers").GetProperty("other").GetProperty("command").GetString());
        Assert.Equal("dark", root.GetProperty("theme").GetString());
        Assert.Equal(@"D:\Moved\TulliusTranslator.exe", root.GetProperty("mcpServers").GetProperty("tullius").GetProperty("command").GetString());

        File.WriteAllText(config, "{ broken");
        Assert.Throws<AgentModeException>(() => AgentMcpServer.InstallInto(config, @"C:\Tullius\TulliusTranslator.exe"));
        Assert.Equal("{ broken", File.ReadAllText(config));
    }

    [Fact]
    public async Task Request_CoversTheRemainingRows_SoTheDialogueAroundALineIsInIt()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        Assert.Equal(0, report.Saved);
        Assert.Empty(await RowsWithStatusAsync(StringEntryStatus.InProgress));
        await Assert.ThrowsAsync<AgentModeException>(() => _session.NextAsync(_project, CancellationToken.None));

        var request = await AnswerUntilAsync(report, r => Items(r).Any(item => item.Text.StartsWith("Your loyalty", StringComparison.Ordinal)));

        // The next line of the scene is not translated in this request (batch size 2), but it is the first line's context.
        Assert.DoesNotContain(Items(request), item => item.Text.StartsWith("Then we have", StringComparison.Ordinal));
        Assert.Contains("Then we have an understanding.", UserText(request));
    }

    /// <summary>
    /// IDE Thalmor's first request: the run saved a line from the game dictionary first, then asked with that line still
    /// in its neighbor's context. The next run (the one an answer is replayed in) leaves saved lines out of the context,
    /// so it asked differently and the answer was dropped.
    /// </summary>
    [Fact]
    public async Task FirstRequest_AfterTheRunSavedGameLines_IsTheOneTheNextRunAsks()
    {
        var game = GameStringDictionary.Build(new[]
        {
            new GameStringPlugin("Skyrim.esm", Array.Empty<string>(), new[]
            {
                new PluginField("INFO/00001002/0/NAM1/0", 0, "INFO", "NAM1", 0x1002, null, 0, 0, "Then we have an understanding.",
                    PluginStringTableKind.IlStrings, 1),
            }, new Dictionary<(PluginStringTableKind, uint), string> { [(PluginStringTableKind.IlStrings, 1)] = "그럼 서로 이해한 걸로 하지." }),
        });
        using var saved = new MemoryStream();
        game.Save(saved);
        var bytes = saved.ToArray();
        var session = new AgentSession(Path.Combine(_root, "work-game"), _global,
            new GameStringService(Path.Combine(_root, "game"), () => new MemoryStream(bytes)), new AppSettings(),
            projectsRootOverride: Path.Combine(_root, "projects-game"));
        // Dialogue only, so the first request is the one with the saved line in its context.
        var plugin = Path.Combine(_root, "Mod", "Dialogue.esp");
        await File.WriteAllBytesAsync(plugin, Header().Concat(DialogueGroup(TopicChildren(0x1000,
            Record("INFO", 0x1001, Sub("NAM1", Z("Your loyalty to the Dominion has been noted."))),
            Record("INFO", 0x1002, Sub("NAM1", Z("Then we have an understanding."))),
            Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>.")))))).ToArray());
        var (project, _, _, _) = await session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
        _extraDbs.Add(project.DbPath);

        var report = await session.NextAsync(project, CancellationToken.None);
        Assert.Equal(1, report.Saved);

        report = await session.SubmitAsync(project, Answer(report.Next!), CancellationToken.None);

        Assert.Null(report.Problem);
        Assert.True(report.Saved > 0);
    }

    /// <summary>
    /// Lanes: each sub-agent runs its own share of the rows (a dialogue kept whole) with its own request, and an
    /// answer stays good while other lanes save theirs, even when a saved row names something its lines mention.
    /// </summary>
    [Fact]
    public async Task Lanes_SplitTheRows_AndAnAnswerSurvivesTheOtherLanesSaves()
    {
        var plugin = Path.Combine(_root, "Mod", "Echo.esp");
        await File.WriteAllBytesAsync(plugin, Header().Concat(DialogueGroup(
                TopicChildren(0x1000,
                    Record("INFO", 0x1001, Sub("NAM1", Z("I told Drelorea that the road is clear."))),
                    Record("INFO", 0x1002, Sub("NAM1", Z("We will find Drelorea at the inn.")))),
                TopicChildren(0x1100,
                    Record("INFO", 0x1101, Sub("NAM1", Z("You should ask Drelorea about the old road."))))))
            .ToArray());
        var (project, _, _, _) = await _session.OpenAsync(plugin, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);
        var one = project with { Lane = AgentLane.Parse("1/2") };
        var two = project with { Lane = AgentLane.Parse("2/2") };

        var first = await _session.NextAsync(one, CancellationToken.None);
        // The lane count is written once, not by every step of every lane (two lanes met on the file and one step failed).
        var lanesFile = Path.Combine(_root, "work", project.Key, "lanes.json");
        File.SetLastWriteTimeUtc(lanesFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = await _session.NextAsync(two, CancellationToken.None);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(lanesFile));

        var ids1 = Ids(first.Next!);
        var ids2 = Ids(second.Next!);
        Assert.Equal(2, ids1.Count);
        Assert.Single(ids2);
        Assert.Empty(ids1.Intersect(ids2));
        Assert.NotEqual(_session.RequestPath(one), _session.RequestPath(two));
        Assert.Throws<AgentModeException>(() => AgentLane.Parse("3/2"));
        await Assert.ThrowsAsync<AgentModeException>(() => _session.StatusAsync(project with { Lane = AgentLane.Parse("1/3") }, CancellationToken.None));

        // Lane 2 saves its spelling of the name first; lane 1's answer, written before, is still the one its run asks for.
        var done2 = await _session.SubmitAsync(two, JsonSerializer.Serialize(new { translations = new[] { new { id = ids2[0], text = "드렐로레아에게 옛길에 대해 물어봐." } } }), CancellationToken.None);
        Assert.Null(done2.Problem);
        Assert.Null(done2.Next);
        var done1 = await _session.SubmitAsync(one, Answer(first.Next!), CancellationToken.None);
        Assert.Null(done1.Problem);
        Assert.Equal(2, done1.Saved);
        Assert.Null(done1.Next);

        // A different number of lanes is refused while a lane has a request waiting, and fine once none has.
        await _session.StatusAsync(project with { Lane = AgentLane.Parse("1/3") }, CancellationToken.None);
    }

    /// <summary>
    /// Review mode: an existing Korean release fills the rows it translates, which the agent then reviews; a row it lacks
    /// stays 대기 and is the only one the next request asks for.
    /// </summary>
    [Fact]
    public async Task KoreanRelease_FillsItsRows_AndOnlyTheRestAreAsked()
    {
        var korean = Path.Combine(_root, "Korean", "Thalmor.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        await File.WriteAllBytesAsync(korean, Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("자치령에 대한 네 충성은 기록해 두었다."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("그럼 서로 이해한 걸로 하지."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("Aetherial Echo")))))
            .ToArray());

        var link = await _session.LinkKoreanAsync(_project, korean, null, CancellationToken.None);

        Assert.Equal(2, link.Filled);
        Assert.Equal(2, link.Untranslated);
        Assert.Empty(link.DifferentFromGame);
        Assert.Equal(2, link.Match.NotTranslated + link.Match.SameAsSource);
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            Assert.Contains(rows, r => r.Status == StringEntryStatus.Done && r.DestText == "그럼 서로 이해한 걸로 하지.");
        }

        var report = await _session.NextAsync(_project, CancellationToken.None);
        var asked = Items(report.Next!).Select(item => item.Text).ToList();
        Assert.Equal(2, report.PendingBefore);
        Assert.DoesNotContain(asked, text => text.StartsWith("Then we have", StringComparison.Ordinal));

        // With a request waiting, the rows it covers are not filled under it.
        await Assert.ThrowsAsync<AgentModeException>(() => _session.LinkKoreanAsync(_project, korean, null, CancellationToken.None));
    }

    /// <summary>
    /// The release's lines were written as they were: one whose tags no longer fit the row was 완료 until the check or the
    /// export refused it, and an old TM fallback note stayed on a filled row, so the check called the release's line "TM
    /// 대신 새로 번역". They get what the run's fill gives an earlier release: a line without the row's alias tag is left
    /// to translate (with the fallback note saying why), and a filled row has the release note alone.
    /// </summary>
    [Fact]
    public async Task KoreanRelease_LeavesALineWithoutTheRowsTags_AndAFilledRowHasNoFallbackNote()
    {
        var korean = Path.Combine(_root, "Korean-tags", "Thalmor.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        await File.WriteAllBytesAsync(korean, Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("자치령에 대한 네 충성을(를) 기록해 두었다."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("그럼 서로 이해한 걸로 하지."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("말조심해라."))))))
            .ToArray());
        long Id(IEnumerable<StringEntry> rows, string english) => rows.Single(r => r.SourceText == english).Id;
        long tagless, filled;
        string taglessBefore;
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            (tagless, filled) = (Id(rows, "Speak with care, <Alias=Player>."), Id(rows, "Then we have an understanding."));
            taglessBefore = rows.Single(r => r.Id == tagless).DestText ?? "";
            // Left by an earlier run whose TM line did not fit (a stopped run leaves it on a 대기 row).
            await db.UpsertStringNoteAsync(filled, XTranslatorAi.Core.TranslationConstants.TmFallbackNoteKind, "TM 폴백: 예전 실행", CancellationToken.None);
        }

        var link = await _session.LinkKoreanAsync(_project, korean, null, CancellationToken.None);

        Assert.Equal(new[] { tagless }, link.TagMismatch);
        Assert.Equal(2, link.Filled);
        Assert.Equal(0, link.KeptTranslated);
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var rows = (await db.GetStringsAsync(50, 0, CancellationToken.None)).ToDictionary(r => r.Id);
            var fallback = await db.GetStringNotesByKindAsync(XTranslatorAi.Core.TranslationConstants.TmFallbackNoteKind, CancellationToken.None);
            var release = await db.GetStringNotesByKindAsync(XTranslatorAi.Core.TranslationConstants.PreviousReleaseNoteKind, CancellationToken.None);
            Assert.Equal((StringEntryStatus.Pending, taglessBefore), (rows[tagless].Status, rows[tagless].DestText ?? ""));
            Assert.StartsWith("이전 번역 폴백:", fallback[tagless]);
            Assert.Equal((StringEntryStatus.Done, "그럼 서로 이해한 걸로 하지."), (rows[filled].Status, rows[filled].DestText));
            Assert.False(fallback.ContainsKey(filled));
            Assert.Equal("기존 한국어판(Thalmor.esp) 그대로, 검수 전", release[filled]);
            // The release's "충성을(를)" is filled as a model's answer would be, and its note says the line was changed.
            var adjusted = Assert.Single(link.Adjusted);
            Assert.Equal("자치령에 대한 네 충성을 기록해 두었다.", rows[adjusted].DestText);
            Assert.StartsWith("기존 한국어판(Thalmor.esp)을 조사·형식만 고쳐 넣음", release[adjusted]);
            Assert.Equal(rows.Values.Count(r => r.Status == StringEntryStatus.Pending), link.Untranslated);
        }
    }

    /// <summary>
    /// A hand edit saved before 1.16.18 kept its TM note, and reopening the plugin brought every note of a kept row back:
    /// the edited name stayed off the names list. A reopen keeps a hand edit's marks only, as an XML reimport does.
    /// </summary>
    [Fact]
    public async Task Reopen_DropsAHandEditsStaleTmNote_AndKeepsTheUsersMark()
    {
        const string english = "Aetherial Echo";
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var id = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.SourceText == english).Id;
            await db.UpdateStringTranslationAsync(id, "에테르 메아리", StringEntryStatus.Edited, null, CancellationToken.None);
            await db.UpsertStringNoteAsync(id, XTranslatorAi.Core.TranslationConstants.TmHitNoteKind, "TM 적용", CancellationToken.None);
            await db.UpsertStringNoteAsync(id, XTranslatorAi.Core.TranslationConstants.SourceFixNoteKind, "원문 오류 수정", CancellationToken.None);
        }

        await _session.OpenAsync(_project.PluginPath, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);

        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var row = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.SourceText == english);
            Assert.Equal((StringEntryStatus.Edited, "에테르 메아리"), (row.Status, row.DestText));
            Assert.False((await db.GetStringNotesByKindAsync(XTranslatorAi.Core.TranslationConstants.TmHitNoteKind, CancellationToken.None)).ContainsKey(row.Id));
            Assert.True((await db.GetStringNotesByKindAsync(XTranslatorAi.Core.TranslationConstants.SourceFixNoteKind, CancellationToken.None)).ContainsKey(row.Id));
        }
    }

    /// <summary>
    /// A Korean release made for an earlier version of the mod: a row whose English changed since was filled with the
    /// old text as 완료. With the release's English original, only rows whose English is the same are filled; without
    /// it, rows whose numbers are not the release's are left to translate (with the release's line as a reference).
    /// </summary>
    [Fact]
    public async Task KoreanRelease_LeavesRowsWhoseEnglishChanged()
    {
        static byte[] Bites(string fireName, string fire, string frostName, string frost)
            => Header().Concat(Group(
                    Record("MGEF", 0x3001, Sub("EDID", Z("aaFireBite")), Sub("FULL", Z(fireName)), Sub("DNAM", Z(fire))),
                    Record("MGEF", 0x3002, Sub("EDID", Z("aaFrostBite")), Sub("FULL", Z(frostName)), Sub("DNAM", Z(frost)))))
                .ToArray();
        var korean = Path.Combine(_root, "Korean", "Bites.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(korean)!);
        await File.WriteAllBytesAsync(korean, Bites("불꽃 물기", "15의 화염 피해를 줍니다.", "냉기 물기", "10의 냉기 피해를 줍니다."));
        var english = Path.Combine(_root, "English", "Bites.esp");
        Directory.CreateDirectory(Path.GetDirectoryName(english)!);
        await File.WriteAllBytesAsync(english, Bites("Fire Bite", "Deals 15 fire damage.", "Frost Nip", "Deals 10 frost damage."));
        var current = Bites("Fire Bite", "Deals 25 fire damage.", "Frost Bite", "Deals 10 frost damage.");

        // Without the English original: the magnitude that changed is caught by its number.
        var byNumbers = await OpenAsync("Bites.esp", current);
        var link = await _session.LinkKoreanAsync(byNumbers, korean, null, CancellationToken.None);

        Assert.False(link.ComparedWithEnglish);
        Assert.Equal(3, link.Filled);
        var fireRow = Assert.Single(link.EnglishChanged);
        Assert.Equal(1, link.Untranslated);
        await using (var db = await ProjectDb.OpenOrCreateAsync(byNumbers.DbPath, CancellationToken.None))
        {
            var row = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.Id == fireRow);
            Assert.Equal((StringEntryStatus.Pending, "Deals 25 fire damage."), (row.Status, row.SourceText));
        }

        // With it: the renamed spell is caught too, and the line whose English is the same is filled.
        var byEnglish = await OpenAsync("Bites2.esp", current);
        var compared = await _session.LinkKoreanAsync(byEnglish, korean, null, CancellationToken.None, english);

        Assert.True(compared.ComparedWithEnglish);
        Assert.Equal(2, compared.Filled);
        Assert.Equal(2, compared.EnglishChanged.Count);
        await using (var db = await ProjectDb.OpenOrCreateAsync(byEnglish.DbPath, CancellationToken.None))
        {
            var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
            Assert.Contains(rows, r => r.Status == StringEntryStatus.Done && r.DestText == "10의 냉기 피해를 줍니다.");
            Assert.Contains(rows, r => r.Status == StringEntryStatus.Pending && r.SourceText == "Frost Bite");
        }

        Assert.True(AgentSession.NumbersFit("Deals 10 damage for 10 seconds.", "10초 동안 10의 피해를 줍니다."));
        Assert.False(AgentSession.NumbersFit("Deals 10 damage for 10 seconds.", "10의 피해를 줍니다."));
        // A font size the release changed for Korean is markup, not text; "1,000" is one number.
        Assert.True(AgentSession.NumbersFit("<font size='60'>Deals 10 damage.</font>", "<font size='48'>10의 피해를 줍니다.</font>"));
        Assert.True(AgentSession.NumbersFit("Costs 1,000 gold.", "1000골드가 듭니다."));
        Assert.False(AgentSession.NumbersFit("Costs 1,500 gold.", "1,000골드가 듭니다."));

        // A wrong --english path is refused before anything is linked.
        var wrong = await OpenAsync("Bites3.esp", current);
        await Assert.ThrowsAsync<AgentModeException>(() =>
            _session.LinkKoreanAsync(wrong, korean, null, CancellationToken.None, Path.Combine(_root, "English", "NoSuch.esp")));
        await using (var db = await ProjectDb.OpenOrCreateAsync(wrong.DbPath, CancellationToken.None))
        {
            Assert.Null(await db.GetPreviousTranslationSourceAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Answers_AreSavedOneRequestAtATime_UntilThePluginIsDone()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        var requests = 0;
        while (report.Next != null)
        {
            requests++;
            Assert.True(requests < 20, "the run kept asking");
            report = await _session.SubmitAsync(_project, Answer(report.Next), CancellationToken.None);
            Assert.Null(report.Problem);
        }

        Assert.True(report.Finished);
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var rows = await db.GetStringsAsync(50, 0, CancellationToken.None);
        Assert.All(rows, row => Assert.Equal(StringEntryStatus.Done, row.Status));
        Assert.Contains(rows, row => row.DestText == "번역: Then we have an understanding.");
        Assert.Null(_session.Pending(_project));
    }

    [Fact]
    public async Task Recording_MakesOneCall_AndLeavesNoRowInProgress()
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var outcome = await AgentTranslationRun.RunAsync(db, new AppSettings(),
            new AgentTranslationRun.Options(AgentTranslationRun.DefaultModelProfile, 1, 15000), (_, _) => null, _global, null,
            CancellationToken.None);

        Assert.NotNull(outcome.Next);
        Assert.Equal(1, outcome.GenerationCalls);
        Assert.Null(outcome.Error);
        Assert.Equal(outcome.PendingBefore, outcome.PendingAfter);
        Assert.Empty(await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.InProgress }, CancellationToken.None));
    }

    // The GUI says when the global DB's series TM or name index could not be read; the agent's step reported 번역 완료
    // with only a WARN line in app.log, so it and the user took a run without the official names for a full one.
    [Fact]
    public async Task UnreadableGlobalTm_IsInTheStepReport()
    {
        var global = await _global.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None);
        Assert.NotNull(global);
        var path = ProjectPaths.GetGlobalGlossaryDbPath(BethesdaFranchise.ElderScrolls, Path.Combine(_root, "global"));
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE TranslationMemory;";
            await drop.ExecuteNonQueryAsync();
        }

        var report = await _session.NextAsync(_project, CancellationToken.None);

        Assert.Contains("시리즈 TM", report.MissingGlobalData);
        Assert.Contains("TM 이름 색인", report.MissingGlobalData);
        Assert.Null(report.Problem);
    }

    // Lanes run at the same time in their own processes: a lane's run used to put the other lane's 번역 중 batch back
    // to 대기, so that lane's split dropped the rows and its answer no longer fit.
    [Fact]
    public async Task LaneRun_LeavesTheOtherLanesRowsInProgress()
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        var otherLane = ids[^1];
        await db.UpdateStringStatusesAsync(new[] { otherLane }, StringEntryStatus.InProgress, null, CancellationToken.None);

        var options = new AgentTranslationRun.Options(AgentTranslationRun.DefaultModelProfile, 1, 15000) { Lane = ids.Take(ids.Count - 1).ToHashSet() };
        var outcome = await AgentTranslationRun.RunAsync(db, new AppSettings(), options, (_, _) => null, _global, null, CancellationToken.None);

        Assert.NotNull(outcome.Next);
        Assert.Equal(new[] { otherLane }, await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.InProgress }, CancellationToken.None));
    }

    // A misnamed key ({"id": 12, "translation": ".."}) read as no text, and the row was saved empty as 직접 수정.
    [Fact]
    public void EditWithoutText_IsRefused()
    {
        var edits = System.Text.Json.JsonSerializer.Deserialize<List<AgentCli.EditLine>>(
            """[{"id": 12, "translation": "새 이름"}, {"order": 3, "text": " "}, {"id": 14, "text": "고친 번역"}]""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(new[] { "id 12", "순번 3" }, AgentCli.WithoutText(edits));
    }

    [Fact]
    public async Task AnswerNotInTheRequestedFormat_IsRefused_AndTheRequestStays()
    {
        var request = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);

        var error = await Assert.ThrowsAsync<AgentModeException>(() => _session.SubmitAsync(_project, "번역했습니다!", CancellationToken.None));

        Assert.Contains("형식", error.Message);
        Assert.Equal(request.Hash, _session.Pending(_project)?.Hash);
    }

    /// <summary>The rows changed after the request was written: the answer fits no call of the new run.</summary>
    [Fact]
    public async Task AnswerToAStaleRequest_IsNotWritten_AndANewRequestIsMade()
    {
        var first = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.UpdateStringTranslationAsync(Ids(first)[0], "사용자가 고친 번역", StringEntryStatus.Edited, null, CancellationToken.None);
        }

        var report = await _session.SubmitAsync(_project, Answer(first), CancellationToken.None);

        Assert.Contains("쓰지 않았습니다", report.Problem);
        Assert.NotNull(report.Next);
        Assert.NotEqual(first.Hash, report.Next!.Hash);
        await using var check = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var rows = await check.GetStringsAsync(50, 0, CancellationToken.None);
        Assert.DoesNotContain(rows, row => Ids(first).Contains(row.Id) && row.DestText.StartsWith("번역:", StringComparison.Ordinal));
    }

    /// <summary>An answer that drops a line's tag is never saved with the tag missing.</summary>
    [Fact]
    public async Task AnswerThatDropsATag_DoesNotSaveTheLineWithoutIt()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        for (var i = 0; i < 20 && report.Next != null; i++)
        {
            var request = report.Next;
            var answer = Answer(request, dropTokens: true);
            report = await _session.SubmitAsync(_project, answer, CancellationToken.None);
        }

        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        var tagged = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(row => row.SourceText.Contains("<Alias=Player>"));
        Assert.True(tagged.Status != StringEntryStatus.Done || tagged.DestText.Contains("<Alias=Player>", StringComparison.Ordinal),
            $"saved without the tag: {tagged.DestText}");
    }

    /// <summary>
    /// A line the run gave up on (오류) is asked with the same request next time. Its stored answers were replayed and
    /// refused again, so `next` said "남은 줄 1 (오류 1)" with no request, however often it was called. Once nothing is
    /// left to ask, the answers the run used are dropped and `next` asks the line afresh.
    /// </summary>
    [Fact]
    public async Task LineLeftInError_IsAskedAfresh_ByTheNextNext()
    {
        // The model keeps dropping the magnitude the check asks for, until the run gives the line up.
        const string Wrong = "화염 피해를 주고 적을 불태웁니다.";
        var project = await OpenAsync("Magic.esp", MagicPlugin());
        var report = await _session.NextAsync(project, CancellationToken.None);
        for (var i = 0; i < 30 && report.Next is { } request; i++)
        {
            var answer = AgentAnswerCheck.IsJson(request) ? JsonAnswer(request, _ => Wrong)
                : AgentAnswerCheck.IsRepair(request) ? Wrong
                : Wrong + " __XT_PH_9999__";
            report = await _session.SubmitAsync(project, answer, CancellationToken.None);
        }

        Assert.Null(report.Next);
        await using (var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None))
        {
            Assert.NotEmpty(await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Error }, CancellationToken.None));
        }

        var again = await _session.NextAsync(project, CancellationToken.None);

        Assert.NotNull(again.Next);
        Assert.Equal(0, again.Next!.Attempt);
    }

    /// <summary>
    /// "&lt;mag&gt;초 동안 화염 피해를" reads as a duration to the run's placeholder check: the line is asked again,
    /// and submit and the next request.md say which line and why. A text answer's file-ending newline is not saved.
    /// </summary>
    [Fact]
    public async Task LineTheRunAsksAgain_IsReportedWithItsReason_AndTheFixIsSavedClean()
    {
        var project = await OpenAsync("Magic.esp", MagicPlugin());
        var report = await _session.NextAsync(project, CancellationToken.None);
        var batch = report.Next!;
        Assert.True(AgentAnswerCheck.IsJson(batch));
        var magId = AgentAnswerCheck.Lines(batch).Single(line => line.Text.Contains("<mag>", StringComparison.Ordinal)).Id!.Value;

        report = await _session.SubmitAsync(project, JsonAnswer(batch, _ => "<mag>초 동안 화염 피해를 주고 적을 불태웁니다."), CancellationToken.None);

        var rejected = Assert.Single(report.Rejected);
        Assert.Equal(magId, rejected.Id);
        Assert.Contains("시간으로 읽힙니다", rejected.Reason);
        Assert.Contains("초 동안", rejected.Reason);
        Assert.NotNull(report.Next);
        Assert.Contains("앞 답이 걸린 이유", await File.ReadAllTextAsync(report.NextRequestPath!));

        // The rest of the batch is saved, so a fresh run asks for the line alone; the same mistake there brings the
        // one-line fix request, answered right in a file that ends with a newline.
        var textRejections = new List<AgentRejection>();
        var sawTextRepair = false;
        for (var i = 0; i < 10 && report.Next is { } request; i++)
        {
            const string Fixed = "<mag>의 화염 피해를 주고 적을 불태웁니다.";
            var answer = AgentAnswerCheck.IsJson(request) ? JsonAnswer(request, _ => Fixed)
                : AgentAnswerCheck.IsRepair(request) ? Fixed + "\r\n"
                : "<mag>초 동안 화염 피해를 주고 적을 불태웁니다. __XT_PH_9999__";
            if (!AgentAnswerCheck.IsJson(request) && AgentAnswerCheck.IsRepair(request))
            {
                // The fix request says to rewrite CURRENT, with no end sentinel (the text request's own rule).
                var md = await File.ReadAllTextAsync(report.NextRequestPath!);
                Assert.Contains("`<<<CURRENT`", md);
                Assert.DoesNotContain("끝의 `__XT_PH_9999__` 포함", md);
                Assert.Contains("한 줄 고쳐 쓰기", md);
                sawTextRepair = true;
            }
            report = await _session.SubmitAsync(project, answer, CancellationToken.None);
            textRejections.AddRange(report.Rejected.Where(r => r.Id == null));
        }

        Assert.True(sawTextRepair, "no one-line fix request");
        Assert.Contains(textRejections, r => r.Reason.Contains("시간으로 읽힙니다", StringComparison.Ordinal));
        Assert.True(report.Finished);
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var row = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.Id == magId);
        Assert.Equal("<mag>의 화염 피해를 주고 적을 불태웁니다.", row.DestText);
    }

    /// <summary>
    /// Meridia's Order: line 3184 was asked again only after a book's one-line request, and its reason came with the
    /// book's request instead of its own. A reason now waits for the request that asks its line.
    /// </summary>
    [Fact]
    public void RejectionReason_WaitsForTheRequestThatAsksItsLineAgain()
    {
        static AgentRequest Batch(params long[] ids)
        {
            var payload = JsonSerializer.Serialize(new { source_language = "english", items = ids.Select(id => new { id, text = $"Line {id}." }) });
            return new AgentRequest("h" + ids[0], 0, "agent-app", null, "system", new[] { new AgentMessage("user", "Input JSON:\n" + payload) },
                "application/json", "{}");
        }

        var book = new AgentRequest("book", 0, "agent-app", null, "system", new[] { new AgentMessage("user", "A book's text.") }, null, null);
        var dir = Path.Combine(Path.GetTempPath(), "xt-carry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reason = new AgentRejection(3184, "Line 3184.", "토큰이 빠졌습니다");

            Assert.Empty(AgentSession.CarryRejections(dir, new[] { reason }, book));
            Assert.Empty(AgentSession.CarryRejections(dir, Array.Empty<AgentRejection>(), Batch(10, 11)));
            Assert.Equal(new[] { reason }, AgentSession.CarryRejections(dir, Array.Empty<AgentRejection>(), Batch(3184, 3185)));
            Assert.Empty(AgentSession.CarryRejections(dir, Array.Empty<AgentRejection>(), Batch(3184)));
            Assert.False(File.Exists(Path.Combine(dir, "rejections.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Instructions_AreWrittenOnce_AndRequestMdNamesThem()
    {
        var report = await _session.NextAsync(_project, CancellationToken.None);
        var first = report.Next!;
        var instructions = await File.ReadAllTextAsync(_session.InstructionsPath(_project));
        var requestMd = await File.ReadAllTextAsync(report.NextRequestPath!);

        Assert.Contains(first.SystemInstruction.Trim(), instructions);
        Assert.DoesNotContain(first.SystemInstruction.Trim(), requestMd);
        Assert.Contains("새 절", requestMd);
        Assert.True(requestMd.Length < first.Chars, $"request.md {requestMd.Length} chars, request {first.Chars}");
        Assert.NotEmpty(report.NewInstructionSections);

        report = await _session.SubmitAsync(_project, Answer(first), CancellationToken.None);
        Assert.NotNull(report.Next);
        Assert.Empty(report.NewInstructionSections.Where(id => id.StartsWith("S-", StringComparison.Ordinal)));
        Assert.Contains("앞 요청과 같음", await File.ReadAllTextAsync(report.NextRequestPath!));
    }

    /// <summary>
    /// The GUI checks the prompt before 번역 시작 (PromptConflictLint): a context the agent saved telling the model to leave
    /// the text in English stopped the GUI's run, and was saved without a word. It is refused; one that keeps only names
    /// in English is saved with a warning.
    /// </summary>
    [Fact]
    public async Task Context_ThatWouldStopTheRun_IsRefused_AndOneKeepingNamesIsSavedWithAWarning()
    {
        const string before = "이 모드는 ESO 시대 배경이다.";
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.UpsertProjectContextAsync(before, CancellationToken.None);
        }

        async Task<string?> SavedAsync()
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
            return (await db.TryGetProjectContextAsync(CancellationToken.None))?.ContextText;
        }

        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        async Task<(bool Error, string Text)> ContextAsync(string text)
        {
            var call = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "context", arguments = new { project = "Thalmor", text } },
            });
            var output = new StringWriter();
            Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(call), output));
            var result = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("result");
            return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
        }

        var refused = await ContextAsync(before + " 원문은 번역하지 말 것.");
        Assert.True(refused.Error);
        Assert.Contains("문맥을 저장하지 않았습니다", refused.Text);
        Assert.Contains("\"원문은 번역하지 말 것\"", refused.Text);
        Assert.Equal(before, await SavedAsync());

        var saved = await ContextAsync(before + " SkyUI 같은 제품명은 번역하지 말 것.");
        Assert.False(saved.Error, saved.Text);
        Assert.Contains("경고: \"SkyUI 같은 제품명은 번역하지 말 것\"", saved.Text);
        Assert.Equal(before + " SkyUI 같은 제품명은 번역하지 말 것.", await SavedAsync());
    }

    // A mod set in ESO's time needs its setting and names before the first request, and both lived only in the GUI,
    // whose "생성한 프로젝트 문맥 사용" box also decided whether agent mode read the context at all.
    [Fact]
    public async Task ContextAndTermsTheAgentSaved_AreInTheRequest_WhateverTheGuiBoxSays()
    {
        var session = new AgentSession(Path.Combine(_root, "work"), _global, gameStrings: null, new AppSettings(EnableProjectContext: false),
            projectsRootOverride: Path.Combine(_root, "projects"));
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.UpsertProjectContextAsync("이 모드는 ESO 시대 배경이다.", CancellationToken.None);
            var saved = await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"source": "Dominion", "target": "알드메리 자치령", "hint": true}]"""), CancellationToken.None);
            Assert.Equal(new[] { "추가: Dominion → 알드메리 자치령 (참고만)" }, saved.Lines);
        }

        var request = (await session.NextAsync(_project, CancellationToken.None)).Next!;

        Assert.Contains("이 모드는 ESO 시대 배경이다.", request.SystemInstruction);
        Assert.Contains("알드메리 자치령", string.Join("\n", request.Messages.Select(m => m.Text)));
    }

    [Fact]
    public async Task Terms_AreOneEntryPerSource_AsTheGuiAddsThem_AndAChangeSaysWhatItHad()
    {
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("""[{"source": "Fury", "target": " "}]"""));
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("[]"));
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("{not json"));
        Assert.Single(AgentCli.ParseTerms("""[{"source": "Fury", "remove": true}]"""));
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);

        await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"source": "Fury", "target": "격노"}, {"source": "Frenzy", "target": "광기", "hint": true}, {"source": "Calm", "target": "진정"}]"""), CancellationToken.None);
        var (lines, changed) = await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms(
            """[{"Source": "fury", "Target": "분노"}, {"source": "Frenzy", "target": "광분", "hint": true}, {"source": "Calm", "target": "진정"}, {"source": "Calm", "target": "진정", "hint": true}, {"source": "Nothing", "remove": true}]"""),
            CancellationToken.None);

        // A hint beside a hint was added next to the old one, which every request kept sending.
        Assert.True(changed);
        Assert.Equal(new[]
        {
            "바꿈: fury → 분노 (강제; 이전: 격노, 강제)", "바꿈: Frenzy → 광분 (참고만; 이전: 광기, 참고만)", "그대로: Calm → 진정",
            "바꿈: Calm → 진정 (참고만; 이전: 강제)", "없음: Nothing (지울 것이 없습니다)",
        }, lines);
        var glossary = await db.GetGlossaryAsync(CancellationToken.None);
        var fury = Assert.Single(glossary, e => e.SourceTerm.Equals("fury", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(("분노", XTranslatorAi.Core.Text.GlossaryForceMode.ForceToken, XTranslatorAi.Core.Text.GlossaryMatchMode.WordBoundary, 10),
            (fury.TargetTerm, fury.ForceMode, fury.MatchMode, fury.Priority));
        Assert.Equal("광분", Assert.Single(glossary, e => e.SourceTerm == "Frenzy").TargetTerm);
        Assert.Equal(XTranslatorAi.Core.Text.GlossaryForceMode.PromptOnly, Assert.Single(glossary, e => e.SourceTerm == "Calm").ForceMode);

        var again = await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"source": "Calm", "target": "진정", "hint": true}]"""), CancellationToken.None);
        Assert.False(again.Changed);
        var removed = await AgentCli.SaveTermsAsync(db, AgentCli.ParseTerms("""[{"source": "FURY", "remove": true}]"""), CancellationToken.None);
        Assert.Equal(new[] { "지움: fury → 분노" }, removed.Lines);
    }

    // Arguments in another shape than the schema's did something else: review's "undo": "true" marked the lines,
    // an answer sent as an object was submitted empty, "ids" as a list showed every line, and a null term crashed.
    [Fact]
    public void McpArguments_InAnotherShape_MeanWhatTheModelMeant()
    {
        static System.Text.Json.Nodes.JsonNode? Node(string json) => System.Text.Json.Nodes.JsonNode.Parse(json);

        Assert.True(AgentMcpServer.ArgumentFlag(Node("\"true\"")));
        Assert.True(AgentMcpServer.ArgumentFlag(Node("1")));
        Assert.True(AgentMcpServer.ArgumentFlag(Node("true")));
        Assert.False(AgentMcpServer.ArgumentFlag(Node("\"false\"")));
        Assert.False(AgentMcpServer.ArgumentFlag(null));
        Assert.Equal("12,40", AgentMcpServer.ArgumentText(Node("[12, 40]")));
        Assert.Equal("40", AgentMcpServer.ArgumentText(Node("40")));
        Assert.Equal("""{"translations":[{"id":1,"text":"검"}]}""", AgentMcpServer.ArgumentText(Node("""{"translations":[{"id":1,"text":"검"}]}""")));
        Assert.Null(AgentMcpServer.ArgumentText(null));
        Assert.Throws<AgentModeException>(() => AgentCli.ParseTerms("""[{"source": "Fury", "target": "분노"}, null]"""));
    }

    // A model wrote the terms as a JSON string, read as no array: the call listed the glossary and looked saved.
    [Fact]
    public void McpArrays_WrittenAsJsonStrings_AreReadAsArrays()
    {
        var terms = AgentMcpServer.AsArray(System.Text.Json.Nodes.JsonValue.Create("""[{"source": "Fury", "target": "분노"}]"""));
        Assert.Equal("Fury", terms![0]!["source"]!.GetValue<string>());
        Assert.Null(AgentMcpServer.AsArray(System.Text.Json.Nodes.JsonValue.Create("Fury")));
        var words = System.Text.Json.Nodes.JsonNode.Parse("""["Fury", 42, {"source": "Mages Guild"}, " "]""")!.AsArray();
        Assert.Equal(new[] { "Fury", "42", "Mages Guild" }, AgentMcpServer.Words(words));
    }

    [Fact]
    public void StyleSharedByLines_IsWrittenOnceAsASection_AndTheLinesNameIt()
    {
        const string described = "REC=*:DESC/DNAM (effect or item description shown in menus). Write every sentence in 합니다체.";
        const string own = "REC=BOOK title. Translate as a book title.";
        var items = new object[]
        {
            new { id = 1, text = "Deals <mag> damage.", rec = "MGEF:DNAM", style = described },
            new { id = 2, text = "Fire Bite", rec = "MGEF:FULL" },
            new { id = 3, text = "Heals <mag> points.", rec = "MGEF:DNAM", style = described },
            new { id = 4, text = "The Lusty Argonian", rec = "BOOK:FULL", style = own },
        };
        var payload = JsonSerializer.Serialize(new { source_language = "english", items },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var user = new string('-', 400) + "\n\nInput JSON:\n" + payload;
        var request = new AgentRequest("hash", 0, "agent-app", null, "system", new[] { new AgentMessage("user", user) },
            "application/json", "{}");

        var parts = AgentRequestRenderer.Split(request);
        var style = Assert.Single(parts.Sections, section => section.Id.StartsWith("ST-", StringComparison.Ordinal));
        Assert.Equal(described, style.Text);
        Assert.DoesNotContain(described, parts.UserPayload);
        Assert.Equal(2, parts.UserPayload.Split($"\"style\":\"[{style.Id}]\"").Length - 1);
        Assert.Contains(own, parts.UserPayload);
        Assert.Contains("합니다체", style.Text);

        var project = new AgentProject("Key", "Mod.esp", "db", new PluginReadOptions(), "utf-8", "agent-app", 30, 15000);
        var rendered = AgentRequestRenderer.Render(project, request, new HashSet<string> { style.Id }, Array.Empty<AgentRejection>());
        Assert.Contains($"- [{style.Id}] 줄 style (새 절)", rendered);
        Assert.DoesNotContain(described, rendered);
        Assert.Contains($"1 MGEF:DNAM | Deals <mag> damage.\n    style: [{style.Id}]", rendered.Replace("\r\n", "\n"));
        Assert.Contains("수치 자리표시자", rendered);
    }

    [Fact]
    public void BatchRequestMd_ListsOneLinePerItem_AndKeepsTheJsonAnswerFormat()
    {
        var items = new object[]
        {
            new { id = 7, text = "Bring me the Stalhrim.\nQuickly.", rec = "INFO:NAM1", edid = "DLC2Quest",
                ctx = "Prev (reference only):\n- Who are you?\nNext (reference only):\n- Fine." },
            new { id = 8, text = "Ratway", rec = "LCTN:FULL" },
        };
        var payload = JsonSerializer.Serialize(new
            {
                source_language = "english", target_language = "korean",
                glossary = new[] { new { source = "Magicka", target = "매지카" }, new { source = "Thalmor", target = "탈모르" } },
                items,
            },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var user = new string('-', 400) + "\n\nReturn JSON schema:\n{\"translations\":[{\"id\":123,\"text\":\"...\"}]}\n\nInput JSON:\n" + payload;
        var request = new AgentRequest("hash", 0, "agent-app", null, "system", new[] { new AgentMessage("user", user) },
            "application/json", "{}");
        var project = new AgentProject("Key", "Mod.esp", "db", new PluginReadOptions(), "utf-8", "agent-app", 30, 15000);

        var rendered = AgentRequestRenderer.Render(project, request, new HashSet<string>(), Array.Empty<AgentRejection>()).Replace("\r\n", "\n");

        Assert.Contains("7 INFO:NAM1 DLC2Quest | Bring me the Stalhrim.\\nQuickly.\n    ctx: Prev: / - Who are you? / Next: / - Fine.\n8 LCTN:FULL | Ratway", rendered);
        Assert.Contains("용어집: Magicka → 매지카; Thalmor → 탈모르", rendered);
        Assert.DoesNotContain("Input JSON:", rendered);
        Assert.DoesNotContain("\"source_language\"", rendered);
        Assert.Contains("{\"translations\":[{\"id\":<요청의 id>,\"text\":\"<번역>\"}, ...]}", rendered);
        // Only what the model reads changes: the request (its hash, what a replay asks) keeps the run's JSON prompt.
        Assert.Contains("Input JSON:\n{\"source_language\"", AgentRequestRenderer.Split(request).UserPayload);
    }

    [Fact]
    public async Task RequestMdOfTheRun_ListsItsLines_WithoutTheInputJson()
    {
        var request = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);
        var requestMd = (await File.ReadAllTextAsync(_session.RequestPath(_project))).Replace("\r\n", "\n");

        Assert.DoesNotContain("Input JSON:", requestMd);
        foreach (var (id, text) in Items(request))
        {
            Assert.Contains($"\n{id} ", requestMd);
            Assert.Contains($" | {text.Replace("\r", "\\r").Replace("\n", "\\n")}\n", requestMd);
        }
    }

    [Fact]
    public async Task Lookup_FindsTheTmSpellingOfAName_TheWordItselfFirst()
    {
        var global = (await _global.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, CancellationToken.None))!;
        await global.BulkUpsertTranslationMemoryAsync("english", "korean", new[]
        {
            ("Stalhrim War Axe", "스탈림 전투 도끼"),
            ("The Stalhrim must be worked with ancient Nord techniques.", "스탈림은 고대 노드의 기법으로 다뤄야 합니다."),
            ("Stalhrim", "스탈림"),
            ("Stalhrimite", "엉뚱한 낱말"),
            ("Ratway", "쥐소굴"),
        }, CancellationToken.None);
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);

        var found = await AgentLookup.FindAsync(db, global, game: null, "stalhrim", 10, CancellationToken.None);

        Assert.Equal(new[] { "Stalhrim", "Stalhrim War Axe", "The Stalhrim must be worked with ancient Nord techniques." },
            found.Memory.Select(h => h.Source));
        Assert.Equal("스탈림", found.Memory[0].Target);
        Assert.Equal(3, found.MemoryTotal);
        Assert.Single((await AgentLookup.FindAsync(db, global, null, "Stalhrim", 1, CancellationToken.None)).Memory);
        Assert.Empty((await AgentLookup.FindAsync(db, global, null, "Concordat", 10, CancellationToken.None)).Memory);

        // The mod's own translated rows: what another lane already called a name.
        var echo = (await db.GetStringsAsync(50, 0, CancellationToken.None)).Single(r => r.SourceText == "Aetherial Echo");
        await db.UpdateStringTranslationAsync(echo.Id, "에테르의 메아리", StringEntryStatus.Done, null, CancellationToken.None);
        var mod = await AgentLookup.FindAsync(db, global, null, "Aetherial", 10, CancellationToken.None);
        Assert.Equal("에테르의 메아리", Assert.Single(mod.Mod!).Target);
        Assert.Equal(1, mod.ModTotal);
    }

    [Fact]
    public async Task AnswerWhoseIdsFitNoLineOfTheRequest_IsRefused()
    {
        var request = await AnswerUntilAsync(await _session.NextAsync(_project, CancellationToken.None), IsJson);

        var error = await Assert.ThrowsAsync<AgentModeException>(() => _session.SubmitAsync(_project,
            "{\"translations\":[{\"id\":987654,\"text\":\"앞 요청의 답\"}]}", CancellationToken.None));

        Assert.Contains("하나도 맞지 않아", error.Message);
        Assert.Equal(request.Hash, _session.Pending(_project)?.Hash);
    }

    /// <summary>
    /// The closing report's new names: translated name fields that no glossary term, TM, game text or Korean release
    /// gave, once per English (EldenWarAshPack1 names each skill in its effect and its scroll). Not yet translated
    /// names and other fields stay out.
    /// </summary>
    [Fact]
    public async Task NewNames_AreTheTranslatedNamesNothingGave_OncePerEnglish()
    {
        var project = await OpenAsync("Names.esp", Header()
            .Concat(Group(
                Record("MISC", 0x4001, Sub("EDID", Z("aaEcho1")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x4002, Sub("EDID", Z("aaEcho2")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x4003, Sub("EDID", Z("aaIron")), Sub("FULL", Z("Iron Ingot"))),
                Record("MISC", 0x4004, Sub("EDID", Z("aaAsh")), Sub("FULL", Z("Ash of War"))),
                Record("MISC", 0x4005, Sub("EDID", Z("aaStorm")), Sub("FULL", Z("Storm Fist"))),
                Record("MISC", 0x4006, Sub("EDID", Z("aaFloat")), Sub("FULL", Z("Learning - Floating Passage"))),
                Record("MISC", 0x4007, Sub("EDID", Z("aaFloat2")), Sub("FULL", Z("Rim Scroll - Floating Passage")))))
            .ToArray());
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var rows = new List<StringEntry>();
        for (var offset = 0; ; offset += 1000)
        {
            var page = await db.GetStringsAsync(1000, offset, CancellationToken.None);
            rows.AddRange(page);
            if (page.Count < 1000) break;
        }

        long Id(string source) => rows.First(r => r.SourceText == source).Id;
        var done = new[]
        {
            ("Aetherial Echo", "에테르의 메아리"), ("Iron Ingot", "주괴 - 강철"), ("Ash of War", "전회"),
            ("Learning - Floating Passage", "배우기 - 쪽배 건너기"), ("Rim Scroll - Floating Passage", "림 주문서 - 쪽배 건너기"),
        };
        await db.UpdateStringTranslationsAsync(rows.Where(r => done.Any(d => d.Item1 == r.SourceText))
            .Select(r => (r.Id, done.First(d => d.Item1 == r.SourceText).Item2, StringEntryStatus.Done, (string?)null)).ToList(), CancellationToken.None);
        await db.UpsertStringNoteAsync(Id("Iron Ingot"), XTranslatorAi.Core.TranslationConstants.TmHitNoteKind, "TM", CancellationToken.None);
        await db.BulkInsertGlossaryAsync(new[] { ((string?)null, "Ash of War", "전회", true, 10, 0, 0, (string?)null) }, CancellationToken.None);
        rows.Clear();
        rows.AddRange(await db.GetStringsAsync(1000, 0, CancellationToken.None));

        var names = await AgentSession.NewNamesAsync(db, rows, CancellationToken.None);

        Assert.Equal(new[] { "Aetherial Echo", "Learning - Floating Passage" }, names.Select(n => n.SourceText));
    }

    /// <summary>
    /// Names inside a text, as the LotD book test's bodies had them: runs of capitalized words joined by "of the",
    /// without the sentence's first word ("During the War of Betony"), a possessive or a lone word that only starts a
    /// sentence (Greetings, The Barber).
    /// </summary>
    [Theory]
    [InlineData("During the War of Betony, the Bretons in the Isle of Craghold were under siege.", "War of Betony|Bretons|Isle of Craghold")]
    [InlineData("Greetings, my friend. Auryen Morellus writes from the Dragonborn Gallery to Shalidor's heirs.", "Auryen Morellus|Dragonborn Gallery|Shalidor")]
    [InlineData("<p align=\"center\">The Barber\n\n[pagebreak]In the market district of Cyrodiil, there once was a barber. I am sure.", "Cyrodiil")]
    [InlineData("What is the thinnest book? Redguard Heroes of the War of Betony.", "Redguard Heroes of the War of Betony")]
    public void TextNames_AreTheCapitalizedRunsThatReadAsNames(string text, string expected)
    {
        Assert.Equal(expected.Split('|'), AgentTextNames.Candidates(text));
    }

    /// <summary>
    /// names reports the names inside translated bodies that nothing knows: a glossary term (Shalidor) and a word the
    /// texts also write in lowercase (Order) stay out, and rows not yet translated are not read.
    /// </summary>
    [Fact]
    public async Task TextNames_AreTheBodyNamesNothingKnows()
    {
        var project = await OpenAsync("Books.esp", Header()
            .Concat(Group(
                Record("BOOK", 0x4011, Sub("EDID", Z("aaNote")), Sub("FULL", Z("A Letter")),
                    Sub("DESC", Z("Greetings, my friend. Auryen Morellus writes from the Dragonborn Gallery about Shalidor and the Order. Our order is old."))),
                Record("BOOK", 0x4012, Sub("EDID", Z("aaLater")), Sub("FULL", Z("Later")),
                    Sub("DESC", Z("Nobody has read what Brelyna Maryon wrote here yet, so it is not reported.")))))
            .ToArray());
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var rows = (await db.GetStringsAsync(1000, 0, CancellationToken.None)).ToList();
        var letter = rows.First(r => r.SourceText.StartsWith("Greetings", StringComparison.Ordinal));
        await db.UpdateStringTranslationsAsync(new[] { (letter.Id, "안녕하신가. 오리엔 모렐루스가 드래곤본 갤러리에서 샬리도어와 교단에 대해 쓰네.", StringEntryStatus.Done, (string?)null) },
            CancellationToken.None);
        await db.BulkInsertGlossaryAsync(new[] { ((string?)null, "Shalidor", "샬리도어", true, 10, 0, 0, (string?)null) }, CancellationToken.None);
        rows = (await db.GetStringsAsync(1000, 0, CancellationToken.None)).ToList();

        var names = await AgentTextNames.FindAsync(db, null, null, rows, CancellationToken.None);

        Assert.Equal(new[] { "Auryen Morellus", "Dragonborn Gallery" }, names.Select(n => n.Name));
        Assert.Equal(new[] { letter.Id }, names[0].Ids);
    }

    /// <summary>
    /// A topic's text (DIAL:FULL) is the player's line, not a name: the names list counted 9,898 of them in 19 projects,
    /// and as "known" text it hid the names it said from the body names. It is read for names as a body is now.
    /// </summary>
    [Fact]
    public async Task ATopicsText_IsNoName_ButIsReadForNames()
    {
        var project = await OpenAsync("Topics.esp", Header()
            .Concat(DialogueGroup(
                Record("DIAL", 0x5000, Sub("EDID", Z("aaAskTopic")), Sub("FULL", Z("Tell me about Auryen Morellus"))),
                TopicChildren(0x5000, Record("INFO", 0x5001, Sub("NAM1", Z("He keeps the gallery."))))))
            .Concat(Group(Record("MISC", 0x5101, Sub("EDID", Z("aaLamp")), Sub("FULL", Z("Shadow Lantern")))))
            .ToArray());
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        var rows = await db.GetAllStringsAsync(CancellationToken.None);
        await db.UpdateStringTranslationsAsync(new[]
        {
            (rows.Single(r => r.SourceText.StartsWith("Tell me", StringComparison.Ordinal)).Id, "오리엔 모렐루스에 대해 말해 줘", StringEntryStatus.Done, (string?)null),
            (rows.Single(r => r.SourceText == "Shadow Lantern").Id, "그림자 등불", StringEntryStatus.Done, (string?)null),
        }, CancellationToken.None);
        rows = await db.GetAllStringsAsync(CancellationToken.None);

        Assert.Equal(new[] { "Shadow Lantern" }, (await AgentSession.NewNamesAsync(db, rows, CancellationToken.None)).Select(n => n.SourceText));
        Assert.Equal(new[] { "Auryen Morellus" }, (await AgentTextNames.FindAsync(db, null, null, rows, CancellationToken.None)).Select(n => n.Name));
    }

    /// <summary>A new plugin of the same mod takes the glossary of the mod's other projects, and open says how many.</summary>
    [Fact]
    public async Task Open_SaysHowManyGlossaryTermsANewProjectTookFromTheSameMod()
    {
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.BulkInsertGlossaryAsync(new[] { ((string?)null, "Thalmor Justiciar", "탈모어 집행관", true, 10, 0, 0, (string?)null) }, CancellationToken.None);
        }

        var path = Path.Combine(_root, "Mod", "Thalmor Addon.esp");
        await File.WriteAllBytesAsync(path, Plugin());
        var (project, _, _, glossary) = await _session.OpenAsync(path, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);

        Assert.Equal(1, glossary.Count);
        Assert.Equal(1, glossary.Inherited);
        Assert.Equal(new[] { "Thalmor.esp" }, glossary.From);
    }

    /// <summary>
    /// A user's Codex refused a whole mod over one character's age, and a line left out of an answer was only asked
    /// again. A held line is out of the waiting request made again and of every request after; the run finishes without
    /// it, the export keeps its English, and released it is asked for again.
    /// </summary>
    [Fact]
    public async Task HeldLine_IsLeftOutOfRequests_TheRunFinishesWithoutIt_AndReleasedItIsAskedAgain()
    {
        var first = await _session.NextAsync(_project, CancellationToken.None);
        var held = Ids(first.Next!)[0];

        await _session.HoldAsync(_project, new[] { held }, " 모델 정책상 번역할 수 없음 ", CancellationToken.None);
        var (remade, _, _) = await _session.RemakePendingAsync(_project, CancellationToken.None);

        var line = Assert.Single(await _session.HeldAsync(_project, CancellationToken.None));
        Assert.Equal((held, StringEntryStatus.Skipped, "보류: 모델 정책상 번역할 수 없음"), (line.Row.Id, line.Row.Status, line.Label));
        var report = remade!;
        for (var i = 0; i < 20 && report.Next != null; i++)
        {
            Assert.DoesNotContain(held, Ids(report.Next));
            report = await _session.SubmitAsync(_project, Answer(report.Next), CancellationToken.None);
        }

        Assert.Null(report.Next);
        Assert.Equal(0, report.PendingAfter);
        var done = (await RowsWithStatusAsync(StringEntryStatus.Done)).First();
        var document = await PluginReader.ReadAsync(_project.PluginPath, _project.ReadOptions, CancellationToken.None);
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            var keys = await db.GetPluginFieldKeysByStringIdAsync(CancellationToken.None);
            var exported = await db.GetPluginTranslationsForExportAsync(document.Info.Sha256, CancellationToken.None);
            Assert.False(exported.ContainsKey(keys[held]));
            Assert.True(exported.ContainsKey(keys[done]));
        }

        var released = await _session.ReleaseAsync(_project, new[] { held, done }, CancellationToken.None);

        Assert.Equal(new[] { held }, released.Select(r => r.Row.Id));
        Assert.Empty(await _session.HeldAsync(_project, CancellationToken.None));
        Assert.Contains(done, await RowsWithStatusAsync(StringEntryStatus.Done));
        // The one line left is asked as a text request (no ids, a protected token after it): its English is what it asks for.
        var again = (await _session.NextAsync(_project, CancellationToken.None)).Next!;
        Assert.StartsWith(line.Row.SourceText, Assert.Single(AgentAnswerCheck.Lines(again)).Text);
    }

    /// <summary>
    /// The hold was the row's error message, which reopening the plugin (open again, the GUI, a mod update) sets empty:
    /// the line stayed 건너뜀 with nothing to say it was held, and could be neither listed, released nor held again. A
    /// line held again keeps its reason, one line as the list prints it; the GUI's 다시 번역 takes the hold away.
    /// </summary>
    [Fact]
    public async Task HeldLine_StaysHeldWhenThePluginIsOpenedAgain_KeepsItsReason_AndRetranslateReleasesIt()
    {
        var held = (await RowsWithStatusAsync(StringEntryStatus.Pending))[0];
        await _session.HoldAsync(_project, new[] { held }, "나이를\n 잘못 읽었을 수 있음", CancellationToken.None);

        var (reopened, _, _, _) = await _session.OpenAsync(_project.PluginPath, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
        await _session.HoldAsync(reopened, new[] { held }, null, CancellationToken.None);

        var line = Assert.Single(await _session.HeldAsync(reopened, CancellationToken.None));
        Assert.Equal((held, StringEntryStatus.Skipped, "보류: 나이를 잘못 읽었을 수 있음"), (line.Row.Id, line.Row.Status, line.Label));
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            Assert.Equal(1, await db.ResetForRetranslationAsync(new[] { held }, includeEdited: false, CancellationToken.None));
            Assert.Empty(await db.GetStringNotesByKindAsync(XTranslatorAi.Core.TranslationConstants.AgentHoldNoteKind, CancellationToken.None));
        }

        Assert.Empty(await _session.HeldAsync(_project, CancellationToken.None));
        Assert.Contains(held, await RowsWithStatusAsync(StringEntryStatus.Pending));

        // 다시 번역 empties the row's text, and the reopen kept only rows with text: held again, it came back 대기.
        await _session.HoldAsync(_project, new[] { held }, "다시", CancellationToken.None);
        var (again, _, _, _) = await _session.OpenAsync(_project.PluginPath, new PluginReadOptions(), "korean", "utf-8", batchSize: 2, CancellationToken.None);
        Assert.Equal("보류: 다시", Assert.Single(await _session.HeldAsync(again, CancellationToken.None)).Label);

        // An edit translates the line: it is held no more, and its note does not come back with the next reopen.
        await using (var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None))
        {
            await db.UpdateStringTranslationAsync(held, "직접 번역함", StringEntryStatus.Edited, null, CancellationToken.None);
            Assert.Empty(await db.GetStringNotesByKindAsync(XTranslatorAi.Core.TranslationConstants.AgentHoldNoteKind, CancellationToken.None));
        }
    }

    /// <summary>
    /// Holding a line also held every waiting line of its English, though a request asks for some of those apart (another
    /// scene, another kind of name) and the model may translate them. They are named, not held, and asked for as before.
    /// </summary>
    [Fact]
    public async Task Hold_NamesTheWaitingLinesOfTheSameEnglishAskedApart_AndLeavesThemToBeAsked()
    {
        var project = await OpenAsync("Twins.esp", Header()
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEchoA")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x2003, Sub("EDID", Z("aaOther")), Sub("FULL", Z("Shadow Lantern")))))
            .Concat(Group(Record("WEAP", 0x2002, Sub("EDID", Z("aaEchoB")), Sub("FULL", Z("Aetherial Echo")))))
            .ToArray());
        var echoes = await IdsOfAsync(project, "Aetherial Echo");
        await _session.NextAsync(project, CancellationToken.None);

        var result = await _session.HoldAsync(project, new[] { echoes[0] }, "이유", CancellationToken.None);

        Assert.Equal(new[] { echoes[0] }, result.Held.Select(h => h.Row.Id));
        Assert.Empty(result.Together);
        Assert.Equal(new[] { echoes[1] }, result.SameEnglishWaiting);
        Assert.Equal(new[] { echoes[0] }, (await _session.HeldAsync(project, CancellationToken.None)).Select(h => h.Row.Id));
        var request = (await _session.RemakePendingAsync(project, CancellationToken.None)).Report!.Next!;
        Assert.Contains(AgentAnswerCheck.Lines(request), line => line.Text.StartsWith("Aetherial Echo", StringComparison.Ordinal));
    }

    /// <summary>
    /// A request asks once for the lines of the same text and context, and gives the others its translation. Holding the
    /// one it asked left the others waiting: the next request asked for the same text again, and the agent held it again.
    /// They are held together, their reason naming the line held, and released with it.
    /// </summary>
    [Fact]
    public async Task Hold_TakesTheLinesTheRequestAskedOnceFor_AndReleasesThemWithIt()
    {
        var project = await OpenAsync("Twins.esp", Header()
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEchoA")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x2002, Sub("EDID", Z("aaEchoB")), Sub("FULL", Z("Aetherial Echo"))),
                Record("MISC", 0x2003, Sub("EDID", Z("aaOther")), Sub("FULL", Z("Shadow Lantern")))))
            .ToArray());
        var echoes = await IdsOfAsync(project, "Aetherial Echo");
        var first = (await _session.NextAsync(project, CancellationToken.None)).Next!;
        Assert.Single(AgentAnswerCheck.Lines(first), line => line.Text.StartsWith("Aetherial Echo", StringComparison.Ordinal));

        // Either of them may be named: the one the request asked, or the one given its translation.
        var result = await _session.HoldAsync(project, new[] { echoes[1] }, "이유", CancellationToken.None);

        Assert.Equal(echoes, result.Held.Select(h => h.Row.Id).OrderBy(id => id));
        Assert.Equal(new[] { echoes[0] }, result.Together);
        Assert.Empty(result.SameEnglishWaiting);
        var held = await _session.HeldAsync(project, CancellationToken.None);
        Assert.Equal($"보류: 이유 (#{echoes[1]} 줄과 함께)", held.Single(h => h.Row.Id == echoes[0]).Label);
        var next = (await _session.RemakePendingAsync(project, CancellationToken.None)).Report!.Next!;
        Assert.DoesNotContain(AgentAnswerCheck.Lines(next), line => line.Text.StartsWith("Aetherial Echo", StringComparison.Ordinal));

        var released = await _session.ReleaseAsync(project, new[] { echoes[1] }, CancellationToken.None);

        Assert.Equal(echoes, released.Select(h => h.Row.Id).OrderBy(id => id));
        Assert.Empty(await _session.HeldAsync(project, CancellationToken.None));

        // Held with no reason, the line held with it is released with it too (its whole reason is the mark).
        await _session.RemakePendingAsync(project, CancellationToken.None);
        await _session.HoldAsync(project, new[] { echoes[0] }, null, CancellationToken.None);
        Assert.Equal($"보류: (#{echoes[0]} 줄과 함께)", (await _session.HeldAsync(project, CancellationToken.None)).Single(h => h.Row.Id == echoes[1]).Label);
        Assert.Equal(echoes, (await _session.ReleaseAsync(project, new[] { echoes[0] }, CancellationToken.None)).Select(h => h.Row.Id).OrderBy(id => id));
    }

    /// <summary>A lane's folder is made with its first request: a lane with nothing to ask finished without one.</summary>
    [Fact]
    public async Task Next_InALaneWithNothingToAsk_FinishesWithoutItsFolder()
    {
        var lane = _project with { Lane = AgentLane.Parse("1/2") };
        foreach (var id in await RowsWithStatusAsync(StringEntryStatus.Pending))
        {
            try
            {
                await _session.HoldAsync(lane, new[] { id }, null, CancellationToken.None);
            }
            catch (AgentModeException)
            {
                // The other lane's line.
            }
        }

        var report = await _session.NextAsync(lane, CancellationToken.None);

        Assert.Null(report.Next);
        Assert.Null(report.Problem);
    }

    private static async Task<List<long>> IdsOfAsync(AgentProject project, string english)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, CancellationToken.None);
        return (await db.GetStringsAsync(10, 0, CancellationToken.None)).Where(r => r.SourceText == english).Select(r => r.Id).OrderBy(id => id).ToList();
    }

    /// <summary>
    /// A held line's text is its English: an edit keeping it ("영어 그대로 두자") was passed over as unchanged, and the
    /// line stayed held. Kept as the user said, it is 직접 수정 and held no more.
    /// </summary>
    [Fact]
    public async Task Edit_KeepingAHeldLinesEnglish_SavesItAndReleasesIt()
    {
        var held = (await RowsWithStatusAsync(StringEntryStatus.Pending))[0];
        var line = (await _session.HoldAsync(_project, new[] { held }, null, CancellationToken.None)).Held.Single();
        var edits = Path.Combine(_root, "keep-english.json");
        await File.WriteAllTextAsync(edits, JsonSerializer.Serialize(new[] { new { id = held, text = line.Row.DestText } }));

        var code = await AgentCli.RunAsync(new[] { "edit", _project.Key, edits, "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") },
            argumentFiles: false);

        Assert.Equal(0, code);
        Assert.Empty(await _session.HeldAsync(_project, CancellationToken.None));
        Assert.Contains(held, await RowsWithStatusAsync(StringEntryStatus.Edited));
    }

    /// <summary>A line with a translation is not held (the export would keep it), nor a list naming a line that is not there.</summary>
    [Fact]
    public async Task Hold_RefusesAFinishedOrMissingLine_AndChangesNothing()
    {
        var first = await _session.NextAsync(_project, CancellationToken.None);
        var second = await _session.SubmitAsync(_project, Answer(first.Next!), CancellationToken.None);
        var done = Ids(first.Next!)[0];
        Assert.Contains(done, await RowsWithStatusAsync(StringEntryStatus.Done));
        var waiting = Ids(second.Next!)[0];

        var finished = await Assert.ThrowsAsync<AgentModeException>(() => _session.HoldAsync(_project, new[] { waiting, done }, null, CancellationToken.None));
        var missing = await Assert.ThrowsAsync<AgentModeException>(() => _session.HoldAsync(_project, new[] { waiting, 99999L }, null, CancellationToken.None));

        Assert.Contains("#" + done, finished.Message);
        Assert.Contains("#99999", missing.Message);
        Assert.Empty(await _session.HeldAsync(_project, CancellationToken.None));
        Assert.DoesNotContain(waiting, await RowsWithStatusAsync(StringEntryStatus.Skipped));
    }

    /// <summary>
    /// A lane holds its own lines only (another lane's waiting request would not be made again, and its answer dropped),
    /// and lists its own: each line is held by exactly one of the two lanes.
    /// </summary>
    [Fact]
    public async Task Hold_InALane_TakesAndListsOnlyThatLanesLines()
    {
        var pending = await RowsWithStatusAsync(StringEntryStatus.Pending);
        var lanes = new[] { _project with { Lane = AgentLane.Parse("1/2") }, _project with { Lane = AgentLane.Parse("2/2") } };
        var heldBy = new Dictionary<long, int>();
        foreach (var id in pending)
        {
            for (var k = 0; k < lanes.Length; k++)
            {
                try
                {
                    await _session.HoldAsync(lanes[k], new[] { id }, null, CancellationToken.None);
                    heldBy.Add(id, k);
                }
                catch (AgentModeException ex)
                {
                    Assert.Contains("갈래 ", ex.Message);
                }
            }
        }

        Assert.Equal(pending.OrderBy(id => id), heldBy.Keys.OrderBy(id => id));
        Assert.Equal(2, heldBy.Values.Distinct().Count());
        for (var k = 0; k < lanes.Length; k++)
        {
            var own = heldBy.Where(pair => pair.Value == k).Select(pair => pair.Key).OrderBy(id => id);
            Assert.Equal(own, (await _session.HeldAsync(lanes[k], CancellationToken.None)).Select(l => l.Row.Id).OrderBy(id => id));
        }
    }

    /// <summary>
    /// The MCP hold tool holds the lines, lists them with their reason without ids, and status counts them. A reason or
    /// --undo with the ids left out showed the list as if it had done something.
    /// </summary>
    [Fact]
    public async Task McpHold_HoldsListsAndCountsTheLines_AndRefusesANoteWithoutIds()
    {
        var held = (await RowsWithStatusAsync(StringEntryStatus.Pending))[0];
        var server = AgentCommand.Parse(new List<string> { "--work", Path.Combine(_root, "work"), "--projects", Path.Combine(_root, "projects"), "--global", Path.Combine(_root, "global") });
        string Call(int id, string name, object arguments) => JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments },
        });
        var output = new StringWriter();
        var lines = string.Join("\n",
            Call(1, "hold", new { project = "Thalmor", ids = held.ToString(), note = "나이 설정 확인 필요" }),
            Call(2, "hold", new { project = "Thalmor" }),
            Call(3, "status", new { project = "Thalmor" }),
            Call(4, "hold", new { project = "Thalmor", note = "이유만" }));

        Assert.Equal(0, await AgentMcpServer.ServeAsync(server, new StringReader(lines), output));

        var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => JsonDocument.Parse(r).RootElement.GetProperty("result")).ToList();
        var texts = replies.Select(r => r.GetProperty("content")[0].GetProperty("text").GetString()!).ToList();
        Assert.Equal(new[] { false, false, false, true }, replies.Select(r => r.GetProperty("isError").GetBoolean()));
        Assert.Contains("보류한 줄: 1.", texts[0]);
        Assert.Contains($"- #{held} ", texts[1]);
        Assert.Contains("보류: 나이 설정 확인 필요", texts[1]);
        Assert.Contains("보류한 줄: 1 (", texts[2]);
        Assert.Contains("--id", texts[3]);
        Assert.Equal("보류: 나이 설정 확인 필요", Assert.Single(await _session.HeldAsync(_project, CancellationToken.None)).Label);
    }

    private async Task<AgentProject> OpenAsync(string name, byte[] plugin)
    {
        var path = Path.Combine(_root, "Mod", name);
        await File.WriteAllBytesAsync(path, plugin);
        var (project, _, _, _) = await _session.OpenAsync(path, new PluginReadOptions(), "korean", "utf-8", batchSize: 5, CancellationToken.None);
        _extraDbs.Add(project.DbPath);
        return project;
    }

    private readonly List<string> _extraDbs = new();

    private static string JsonAnswer(AgentRequest request, Func<string, string> translate)
        => JsonSerializer.Serialize(new
        {
            translations = AgentAnswerCheck.Lines(request).Select(line => new
            {
                id = line.Id,
                text = line.Text.Contains("<mag>", StringComparison.Ordinal) ? translate(line.Text) : "번역: " + line.Text,
            }),
        });

    private static byte[] MagicPlugin()
        => Header().Concat(Group(Record("MGEF", 0x3001, Sub("EDID", Z("aaFireBite")), Sub("FULL", Z("Fire Bite")),
                Sub("DNAM", Z("Deals <mag> fire damage and burns the target.")))))
            .ToArray();

    private async Task<IReadOnlyList<long>> RowsWithStatusAsync(StringEntryStatus status)
    {
        await using var db = await ProjectDb.OpenOrCreateAsync(_project.DbPath, CancellationToken.None);
        return await db.GetStringIdsByStatusAsync(new[] { status }, CancellationToken.None);
    }

    /// <summary>Answers requests until one matches, and returns that one, unanswered and pending.</summary>
    private async Task<AgentRequest> AnswerUntilAsync(AgentStepReport report, Func<AgentRequest, bool> wanted)
    {
        for (var i = 0; i < 20; i++)
        {
            var request = report.Next ?? throw new InvalidOperationException("the run finished first");
            if (wanted(request)) return request;
            report = await _session.SubmitAsync(_project, Answer(request), CancellationToken.None);
        }

        throw new InvalidOperationException("no such request");
    }

    private static bool IsJson(AgentRequest request) => request.ResponseMimeType == "application/json";

    private static string UserText(AgentRequest request) => string.Join("\n", request.Messages.Where(m => m.Role == "user").Select(m => m.Text));

    private static List<(long Id, string Text)> Items(AgentRequest request)
    {
        var text = UserText(request);
        if (!IsJson(request))
        {
            var from = text.IndexOf("<<<TEXT", StringComparison.Ordinal) + "<<<TEXT".Length;
            return new List<(long, string)> { (0, text[from..text.LastIndexOf("TEXT>>>", StringComparison.Ordinal)].Trim('\r', '\n')) };
        }

        var start = text.IndexOf("{\"source_language\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "no payload in the request");
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text[start..]));
        using var payload = JsonDocument.ParseValue(ref reader);
        return payload.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetInt64(), item.GetProperty("text").GetString() ?? "")).ToList();
    }

    private static List<long> Ids(AgentRequest request) => Items(request).Select(item => item.Id).ToList();

    /// <summary>An answer the way the model writes it: every item, its protected tokens kept (unless told to drop them).</summary>
    private static string Answer(AgentRequest request, bool dropTokens = false)
        => !IsJson(request)
            ? "번역: " + Items(request)[0].Text
            : JsonSerializer.Serialize(new
        {
            translations = Items(request).Select(item => new
            {
                id = item.Id,
                text = "번역: " + (dropTokens ? System.Text.RegularExpressions.Regex.Replace(item.Text, "__XT_[A-Z0-9_]+__", "") : item.Text),
            }),
        });

    private static byte[] Plugin()
        => Header().Concat(DialogueGroup(TopicChildren(0x1000,
                Record("INFO", 0x1001, Sub("NAM1", Z("Your loyalty to the Dominion has been noted."))),
                Record("INFO", 0x1002, Sub("NAM1", Z("Then we have an understanding."))),
                Record("INFO", 0x1003, Sub("NAM1", Z("Speak with care, <Alias=Player>."))))))
            .Concat(Group(Record("MISC", 0x2001, Sub("EDID", Z("aaEcho")), Sub("FULL", Z("Aetherial Echo")))))
            .ToArray();

    private static byte[] DialogueGroup(params byte[][] children)
    {
        var bytes = Group(children);
        Encoding.ASCII.GetBytes("DIAL").CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] TopicChildren(uint topicFormId, params byte[][] children)
    {
        var bytes = Group(children);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), topicFormId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 7);
        return bytes;
    }
}
