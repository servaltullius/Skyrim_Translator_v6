using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// The agent mode as an MCP server over stdio (TulliusTranslator.exe agent mcp): the same commands as tullius.cmd, as
/// tools with structured arguments. Answers and fixes go in as arguments and requests come back as results, so no
/// shell quotes Korean, no answer file is written in a code page the translator does not read, and no .cmd splits an
/// argument at "&amp;" (the Codex trial's and the research report's troubles). Each tool runs the CLI command it names
/// with its output captured, so the two give the same results; stdout carries only JSON-RPC.
/// </summary>
internal static class AgentMcpServer
{
    private static readonly string[] ProtocolVersions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

    private const string Instructions =
        "툴리우스 번역기 에이전트 모드 도구다. 스카이림 모드 플러그인을 네가 직접 한국어로 번역하고, 이 도구들은 읽기·보호·검증·저장만 한다. " +
        "절차는 agent-kit의 AGENTS.md와 guide-translate.md(번역), guide-review.md(기존 한국어판 검수), guide-lanes.md(갈래)를 따르되 " +
        "tullius.cmd 대신 이 도구를 쓴다. 파일을 읽을 수 없으면 guide 도구로 읽는다. next·submit 결과에 다음 요청 내용이 함께 오고, 새 지시 절이 있으면 instructions로 읽는다. " +
        "답은 submit의 answer에 요청의 응답 형식 그대로 넣는다(파일을 쓰지 않는다).";

    private sealed record Tool(string Name, string Description, JsonObject Properties, string[] Required);

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };
    private static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };

    private static JsonObject StrArray(string description)
        => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = description };

    private static JsonObject Project() => Str("프로젝트 키, 플러그인 파일 이름(확장자 없이도) 또는 플러그인 경로");
    private static JsonObject Lane() => Str("갈래 번역일 때 '번호/갈래 수'(예: 2/3)");

    private static readonly Tool[] Tools =
    {
        new("open", "플러그인을 열어 프로젝트를 만든다(GUI와 같은 프로젝트). 결과의 '프로젝트:' 키를 다른 도구에 쓴다.",
            new JsonObject { ["plugin"] = Str("플러그인(.esp/.esm/.esl) 전체 경로"), ["strings"] = Str("현지화 플러그인의 Strings 폴더"), ["batch_size"] = Int("요청 하나의 줄 수(기본 30)") },
            new[] { "plugin" }),
        new("status", "진행 상황과 대기 중인 요청.", new JsonObject { ["project"] = Project(), ["lane"] = Lane() }, new[] { "project" }),
        new("next", "첫 요청을 만든다(끊긴 뒤 다시 시작할 때도). 요청 내용이 결과에 함께 온다.",
            new JsonObject { ["project"] = Project(), ["lane"] = Lane() }, new[] { "project" }),
        new("request", "대기 중인 요청(request.md) 내용.", new JsonObject { ["project"] = Project(), ["lane"] = Lane() }, new[] { "project" }),
        new("instructions", "요청들이 쓰는 지시(instructions.md) 내용. 처음 요청과 새 지시 절이 있을 때 읽는다.",
            new JsonObject { ["project"] = Project(), ["lane"] = Lane() }, new[] { "project" }),
        new("submit", "대기 중인 요청에 답한다. 저장한 줄·다시 물을 줄과 다음 요청 내용이 결과에 온다.",
            new JsonObject { ["project"] = Project(), ["answer"] = Str("요청의 응답 형식 그대로: JSON 요청이면 {\"translations\":[{\"id\":..,\"text\":\"..\"}]}, 글 요청이면 번역문만"), ["lane"] = Lane() },
            new[] { "project", "answer" }),
        new("lookup", "이름·용어의 기존 표기(용어집·게임 사전·한글패치 TM·이 모드에서 번역한 줄). 여러 낱말을 한 번에 찾는다.",
            new JsonObject { ["project"] = Project(), ["words"] = StrArray("찾을 영어 낱말들") }, new[] { "project", "words" }),
        new("names", "새로 정한 이름 목록(용어집·TM·게임 사전·한국어판에 없어 번역하며 정한 이름 줄, 책·대사 본문에 나온 새 영어 이름 후보). 끝 보고에 넣는다.",
            new JsonObject { ["project"] = Project() }, new[] { "project" }),
        new("voice", "바닐라 인물이 한글패치에서 하는 말(말투 참고).",
            new JsonObject { ["project"] = Project(), ["who"] = Str("영어 이름, 한국어 이름 또는 FormID") }, new[] { "project", "who" }),
        new("style", "화자별로 정한 말투 보기·정하기. speaker 없이 부르면 목록과 정하지 않은 화자를 보여 준다.",
            new JsonObject
            {
                ["project"] = Project(), ["speaker"] = Str("화자(이름 일부, FormID, player)"), ["level"] = Str("합쇼체·해요체·하오체·하게체·반말"),
                ["to"] = Str("상대(생략하면 플레이어; other, *, 이름, FormID)"), ["note"] = Str("메모(1인칭·호칭 등)"), ["clear"] = Bool("정한 말투를 지운다"),
            },
            new[] { "project" }),
        new("show", "줄 보기(#id, 화자, 영어, 번역).",
            new JsonObject
            {
                ["project"] = Project(), ["find"] = Str("원문이나 번역에 든 글자"), ["rec"] = Str("레코드 종류(예: WEAP:FULL)"),
                ["ids"] = Str("id 목록(예: 12,40)"), ["after"] = Str("이 id 뒤부터"), ["count"] = Int("보여 줄 줄 수"),
            },
            new[] { "project" }),
        new("check", "품질검사.", new JsonObject { ["project"] = Project(), ["all"] = Bool("고칠 것 없는 참고·확인함까지") }, new[] { "project" }),
        new("edit", "줄 고치기.",
            new JsonObject
            {
                ["project"] = Project(),
                ["edits"] = new JsonObject
                {
                    ["type"] = "array", ["description"] = "고칠 줄들",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["id"] = Int("줄 id"), ["text"] = Str("고친 번역") },
                        ["required"] = new JsonArray("id", "text"),
                    },
                },
            },
            new[] { "project", "edits" }),
        new("review", "그대로 둘 줄의 품질검사 결과를 확인함으로 표시(번역문이 바뀌면 다시 나옴).",
            new JsonObject { ["project"] = Project(), ["ids"] = Str("id 목록(예: 12,40)"), ["undo"] = Bool("표시를 지운다") }, new[] { "project", "ids" }),
        new("korean", "기존 한국어판을 연결하고 아직 번역 안 된 줄을 채운다(검수 모드).",
            new JsonObject { ["project"] = Project(), ["korean_plugin"] = Str("한국어판 플러그인 전체 경로"), ["strings"] = Str("한국어판 Strings 폴더"),
                ["english_plugin"] = Str("한국어판이 번역한 영어 원본 플러그인 전체 경로(있으면 영어가 바뀐 줄만 번역)") },
            new[] { "project", "korean_plugin" }),
        new("export", "번역된 플러그인 저장(작업 폴더 옆 output).", new JsonObject { ["project"] = Project() }, new[] { "project" }),
        new("version", "번역기 버전.", new JsonObject(), Array.Empty<string>()),
        new("guide", "agent-kit의 안내 파일 내용(셸로 파일을 읽을 수 없을 때).",
            new JsonObject { ["name"] = Str("rules(AGENTS.md), translate, review, lanes 중 하나") }, new[] { "name" }),
    };

    // The guides beside the translator, by the name the guide tool and the resources give them.
    private static readonly (string Name, string File, string Title)[] Guides =
    {
        ("rules", "AGENTS.md", "늘 지킬 규칙"), ("translate", "guide-translate.md", "번역 절차"),
        ("review", "guide-review.md", "기존 한국어판 검수 절차"), ("lanes", "guide-lanes.md", "갈래(하위 에이전트) 번역 절차"),
    };

    // The folder of the translator itself: a single-file build runs from an extraction folder (AppContext.BaseDirectory),
    // and the 1.16.4 build found no agent-kit there.
    private static string ExeFolder => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    private static string GuidePath(string file) => Path.Combine(ExeFolder, "agent-kit", file);

    private static string GuideUri(string name) => "tullius://guide/" + name;

    /// <summary>
    /// How an app starts this server, with this translator's own path: agent-kit's .mcp.json (Claude) and
    /// .codex\config.toml (Codex) start it from the folder above, but Antigravity and Gemini CLI keep one config for all
    /// folders and need the full path.
    /// </summary>
    public static string SetupText()
    {
        var exe = Environment.ProcessPath ?? Path.Combine(ExeFolder, "TulliusTranslator.exe");
        var json = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["tullius"] = new JsonObject { ["command"] = exe, ["args"] = new JsonArray("agent", "mcp") },
            },
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return $"""
            툴리우스 번역기를 MCP 도구(tullius)로 쓰는 설정입니다. 도구는 tullius.cmd의 명령과 같습니다.

            Claude(Code 탭·Claude Code)와 Codex는 agent-kit 폴더를 열면 그 폴더의 .mcp.json, .codex\config.toml로 바로 씁니다
            (처음 한 번 서버 사용을 허용하거나 폴더를 신뢰한다고 답합니다).

            안티그라비티(MCP 서버 관리 → raw config 보기)와 Gemini CLI(settings.json)는 아래 설정을 넣습니다
            (이미 mcpServers가 있으면 그 안에 tullius 항목만 더합니다). 안티그라비티는 설정 파일이
            %USERPROFILE%\.gemini\config\mcp_config.json이고, 넣은 뒤 앱을 다시 시작해야 도구가 보입니다.

            {json}

            Codex를 다른 폴더에서도 쓰려면: codex mcp add tullius -- "{exe}" agent mcp
            Claude Code를 다른 폴더에서도 쓰려면: claude mcp add --scope user tullius -- "{exe}" agent mcp
            """;
    }

    public static async Task<int> RunAsync(AgentCommand given)
    {
        // An app starts the server from a folder of its own: without --work, the work folder is agent-kit's beside the
        // translator, where tullius.cmd keeps it.
        var kit = Path.Combine(ExeFolder, "agent-kit");
        var server = given.WorkGiven || !Directory.Exists(kit)
            ? given
            : AgentCommand.Parse(new List<string> { "--work", Path.Combine(kit, "work") }
                .Concat(given.ProjectsRoot is { } root ? new[] { "--projects", root } : Array.Empty<string>()).ToList());
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        // stdout is the protocol's: whatever else writes to the console goes to stderr (a tool's output is captured).
        Console.SetOut(Console.Error);
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        Console.Error.WriteLine("tullius MCP 서버 시작: " + server.Work);
        return await ServeAsync(server, stdin, stdout);
    }

    /// <summary>Answers each JSON-RPC message read, one per line, until the input ends.</summary>
    internal static async Task<int> ServeAsync(AgentCommand server, TextReader input, TextWriter output)
    {
        while (await input.ReadLineAsync() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            JsonNode? message;
            try
            {
                message = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                await WriteAsync(output, Error(null, -32700, "JSON을 읽지 못했습니다."));
                continue;
            }

            if (message is not JsonObject request) continue;
            var id = request["id"]?.DeepClone();
            var method = request["method"]?.GetValue<string>();
            if (method == null || id == null) continue; // a notification (initialized, cancelled) or a response: nothing to answer

            JsonObject reply;
            try
            {
                reply = method switch
                {
                    "initialize" => Result(id, Initialize(request["params"] as JsonObject)),
                    "ping" => Result(id, new JsonObject()),
                    "tools/list" => Result(id, new JsonObject { ["tools"] = new JsonArray(Tools.Select(Describe).ToArray<JsonNode?>()) }),
                    "tools/call" => Result(id, await CallAsync(server, request["params"] as JsonObject)),
                    // An app whose shell cannot read the guides (Codex's sandbox failed so in the trial) looks for them here.
                    "resources/list" => Result(id, new JsonObject { ["resources"] = new JsonArray(Guides.Where(g => File.Exists(GuidePath(g.File)))
                        .Select(g => (JsonNode?)new JsonObject { ["uri"] = GuideUri(g.Name), ["name"] = g.File, ["title"] = g.Title, ["mimeType"] = "text/markdown" }).ToArray()) }),
                    "resources/templates/list" => Result(id, new JsonObject { ["resourceTemplates"] = new JsonArray() }),
                    "resources/read" => ReadResource(id, request["params"]?["uri"]?.GetValue<string>()),
                    _ => Error(id, -32601, "지원하지 않는 메서드: " + method),
                };
            }
            catch (Exception ex)
            {
                reply = Error(id, -32603, ex.Message);
            }

            await WriteAsync(output, reply);
        }

        return 0;
    }

    private static JsonObject Initialize(JsonObject? parameters)
    {
        var asked = parameters?["protocolVersion"]?.GetValue<string>();
        return new JsonObject
        {
            ["protocolVersion"] = asked != null && ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[1],
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false }, ["resources"] = new JsonObject { ["listChanged"] = false },
            },
            ["serverInfo"] = new JsonObject { ["name"] = "tullius", ["version"] = AgentCli.VersionText() },
            ["instructions"] = Instructions,
        };
    }

    private static JsonObject ReadResource(JsonNode id, string? uri)
    {
        var guide = Guides.FirstOrDefault(g => GuideUri(g.Name) == uri);
        if (guide.File == null || !File.Exists(GuidePath(guide.File))) return Error(id, -32002, "없는 리소스: " + uri);
        return Result(id, new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["uri"] = uri, ["mimeType"] = "text/markdown", ["text"] = File.ReadAllText(GuidePath(guide.File)) }),
        });
    }

    private static JsonObject Describe(Tool tool)
        => new()
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = tool.Properties.DeepClone(),
                ["required"] = new JsonArray(tool.Required.Select(r => (JsonNode?)r).ToArray()),
            },
        };

    private static async Task<JsonObject> CallAsync(AgentCommand server, JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? "";
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        string? Text(string key) => arguments[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s
            : arguments[key] is JsonValue n ? n.ToJsonString() : null;
        bool Flag(string key) => arguments[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        var project = Text("project");
        var lane = Text("lane");
        var args = new List<string>();
        void Option(string option, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) args.AddRange(new[] { option, value });
        }

        string output;
        bool failed;
        string? temp = null;
        switch (name)
        {
            case "request":
            case "instructions":
            {
                var session = AgentCommand.Parse(ServerOptions(server)).Session;
                var found = session.Find(project ?? "") with { Lane = AgentLane.Parse(lane) };
                var path = name == "request" ? session.RequestPath(found) : session.InstructionsPath(found);
                return Content(File.Exists(path) ? await File.ReadAllTextAsync(path) : name == "request" ? "대기 중인 요청이 없습니다. next로 요청을 만드세요." : "지시 파일이 아직 없습니다. next로 첫 요청을 만드세요.",
                    isError: !File.Exists(path));
            }
            case "lookup":
            {
                var words = arguments["words"] is JsonArray list ? list.Select(w => w?.GetValue<string>()).OfType<string>().ToList() : new List<string>();
                if (Text("word") is { } single) words.Add(single);
                var parts = new List<string>();
                failed = words.Count == 0;
                foreach (var word in words)
                {
                    var (text, code) = await RunAsync(server, new List<string> { "lookup", project ?? "", word });
                    parts.Add(text.TrimEnd());
                    failed |= code != 0;
                }

                return Content(parts.Count == 0 ? "찾을 낱말(words)을 지정하세요." : string.Join("\n\n", parts), failed);
            }
            case "submit":
            {
                var file = temp = TempFile(server, "answer.txt");
                await File.WriteAllTextAsync(file, Text("answer") ?? "", new UTF8Encoding(false));
                args.AddRange(new[] { "submit", project ?? "", file });
                break;
            }
            case "edit":
            {
                var file = temp = TempFile(server, "edit.json");
                await File.WriteAllTextAsync(file, (arguments["edits"] ?? new JsonArray()).ToJsonString(), new UTF8Encoding(false));
                args.AddRange(new[] { "edit", project ?? "", file });
                break;
            }
            case "open":
                args.AddRange(new[] { "open", Text("plugin") ?? "" });
                Option("--strings", Text("strings"));
                Option("--batch-size", Text("batch_size"));
                break;
            case "korean":
                args.AddRange(new[] { "korean", project ?? "", Text("korean_plugin") ?? "" });
                Option("--strings", Text("strings"));
                Option("--english", Text("english_plugin"));
                break;
            case "voice":
                args.AddRange(new[] { "voice", project ?? "", Text("who") ?? "" });
                break;
            case "style":
                args.AddRange(new[] { "style", project ?? "" });
                if (Text("speaker") is { Length: > 0 } speaker)
                {
                    args.Add(speaker);
                    if (Text("level") is { Length: > 0 } level) args.Add(level);
                }

                Option("--to", Text("to"));
                Option("--note", Text("note"));
                if (Flag("clear")) args.Add("--clear");
                break;
            case "show":
                args.AddRange(new[] { "show", project ?? "" });
                Option("--find", Text("find"));
                Option("--rec", Text("rec"));
                Option("--id", Text("ids"));
                Option("--after", Text("after"));
                Option("--count", Text("count"));
                break;
            case "check":
                args.AddRange(new[] { "check", project ?? "" });
                if (Flag("all")) args.Add("--all");
                break;
            case "review":
                args.AddRange(new[] { "review", project ?? "" });
                Option("--id", Text("ids"));
                if (Flag("undo")) args.Add("--undo");
                break;
            case "status":
            case "next":
            case "names":
            case "export":
                args.AddRange(new[] { name, project ?? "" });
                break;
            case "version":
                args.Add("version");
                break;
            case "guide":
            {
                var wanted = (Text("name") ?? "").Trim();
                var guide = Guides.FirstOrDefault(g => g.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase) || g.File.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                return guide.File != null && File.Exists(GuidePath(guide.File))
                    ? Content(await File.ReadAllTextAsync(GuidePath(guide.File)))
                    : Content("없는 안내: " + wanted + " (rules, translate, review, lanes 중 하나)", isError: true);
            }
            default:
                return Content("없는 도구: " + name, isError: true);
        }

        Option("--lane", name is "status" or "next" or "submit" ? lane : null);
        int exit;
        try
        {
            (output, exit) = await RunAsync(server, args);
        }
        finally
        {
            // A file held for a moment (antivirus, the indexer) is left behind rather than turning a saved answer into an error.
            if (temp != null)
            {
                try { File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        failed = exit != 0;

        // next and submit end with the next request: its text comes with the result, so no file needs reading.
        if (name is "next" or "submit" && !failed && output.Contains("다음 요청:", StringComparison.Ordinal))
        {
            var session = AgentCommand.Parse(ServerOptions(server)).Session;
            var found = session.Find(project ?? "") with { Lane = AgentLane.Parse(lane) };
            if (File.Exists(session.RequestPath(found)))
            {
                output = output.TrimEnd() + "\n\n----- 다음 요청 -----\n\n" + await File.ReadAllTextAsync(session.RequestPath(found));
            }
        }

        return Content(output, failed);
    }

    // The CLI command, its console output captured: the result is the same as tullius.cmd gives.
    private static async Task<(string Output, int Exit)> RunAsync(AgentCommand server, List<string> args)
    {
        var captured = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(captured);
        try
        {
            var exit = await AgentCli.RunAsync(args.Concat(ServerOptions(server)).ToArray());
            return (captured.ToString(), exit);
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    private static List<string> ServerOptions(AgentCommand server)
    {
        var options = new List<string> { "--work", server.Work };
        if (server.ProjectsRoot != null) options.AddRange(new[] { "--projects", server.ProjectsRoot });
        return options;
    }

    // Each call has its own file: servers of several lanes (or app sessions) share the work folder, and a fixed
    // answer.txt let one lane's submit overwrite another's between its write and its read, saving lane B's Korean
    // into lane A's one-line request.
    private static string TempFile(AgentCommand server, string name)
    {
        var dir = Path.Combine(server.Work, ".mcp");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}-{Guid.NewGuid():N}{Path.GetExtension(name)}");
    }

    private static JsonObject Content(string text, bool isError = false)
        => new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError };

    private static JsonObject Result(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static Task WriteAsync(TextWriter output, JsonObject message)
        => output.WriteLineAsync(message.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
}
