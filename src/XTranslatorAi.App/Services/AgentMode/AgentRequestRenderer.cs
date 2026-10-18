using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// Writes a request for the model to read. The run's instructions (the system prompt, the rules at the head of the
/// user prompt, the response schema) are the same request after request, so they go once into
/// <c>instructions.md</c> as sections named by their content hash, and <c>request.md</c> names the sections it uses
/// and holds only what changes: the lines to translate, their style, glossary and context. A style that several lines
/// of a batch share (a 300-character hint on every effect description) is a section too, and the line names it. Only
/// what the model reads is split; the request (its hash, what is replayed) is the run's prompt as it is.
/// </summary>
public static class AgentRequestRenderer
{
    public const string InstructionsFileName = "instructions.md";

    // A payload section of a user prompt starts with one of these lines after a blank line; what comes before is rules.
    private static readonly string[] PayloadStarts =
    {
        "Input JSON:", "Style:", "Text to translate:", "SOURCE:", "Glossary (preferred", "- 'edid' is an internal EditorID",
    };

    private const int MinimumSplit = 300;

    public sealed record Section(string Id, string Title, string Text);

    /// <summary>The request split into the shared sections and the request's own text.</summary>
    public sealed record Parts(IReadOnlyList<Section> Sections, string UserPayload);

    public static Parts Split(AgentRequest request)
    {
        var sections = new List<Section>();
        if (!string.IsNullOrWhiteSpace(request.SystemInstruction))
        {
            sections.Add(new Section(Id("S", request.SystemInstruction), "지시", request.SystemInstruction.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.ResponseSchema))
        {
            sections.Add(new Section(Id("J", request.ResponseSchema), "응답 스키마", "```json\n" + request.ResponseSchema.Trim() + "\n```"));
        }

        var user = string.Join("\n\n", request.Messages.Where(m => m.Role == "user").Select(m => m.Text));
        var (rules, payload) = SplitUserPrompt(user);
        if (rules != null)
        {
            sections.Add(new Section(Id("R", rules), "요청 규칙", rules));
        }

        return new Parts(sections, NameSharedStyles(payload, sections));
    }

    /// <summary>
    /// In the batch's Input JSON, replaces a <c>style</c> that two or more items share with the name of a section
    /// holding it (<c>[ST-…]</c>), so the hint is read once instead of once per line. A style only one line has stays
    /// where it is: it is often that line's own note, and as a section it would only make the file longer.
    /// </summary>
    internal static string NameSharedStyles(string payload, List<Section> sections)
    {
        var lines = payload.Split('\n');
        var at = Array.FindIndex(lines, line => line.TrimEnd() == "Input JSON:");
        if (at < 0 || at + 1 >= lines.Length) return payload;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(lines[at + 1]);
        }
        catch (JsonException)
        {
            return payload;
        }

        if (root?["items"] is not JsonArray items) return payload;
        var shared = items.OfType<JsonObject>()
            .Select(item => (Item: item, Style: item["style"] is JsonValue value && value.TryGetValue(out string? style) ? style : null))
            .Where(x => !string.IsNullOrWhiteSpace(x.Style))
            .GroupBy(x => x.Style!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        if (shared.Count == 0) return payload;

        foreach (var group in shared)
        {
            var section = new Section(Id("ST", group.Key), "줄 style", group.Key.Trim());
            if (sections.All(s => s.Id != section.Id)) sections.Add(section);
            foreach (var (item, _) in group) item["style"] = $"[{section.Id}]";
        }

        lines[at + 1] = root.ToJsonString(PayloadJson);
        return string.Join("\n", lines);
    }

    // As the run writes the batch payload: Hangul, '<mag>' and apostrophes as they are, not as escape sequences.
    private static readonly JsonSerializerOptions PayloadJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static (string? Rules, string Payload) SplitUserPrompt(string user)
    {
        var lines = user.Replace("\r\n", "\n").Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i - 1].Trim().Length != 0 || !PayloadStarts.Any(start => lines[i].StartsWith(start, StringComparison.Ordinal)))
            {
                continue;
            }

            var rules = string.Join("\n", lines[..i]).Trim();
            if (rules.Length < MinimumSplit) break;
            return (rules, string.Join("\n", lines[i..]).Trim());
        }

        return (null, user.Trim());
    }

    /// <summary>
    /// Adds the sections the file lacks to <c>instructions.md</c> and returns the ids it did not have before (the
    /// ones the model has not read yet).
    /// </summary>
    public static IReadOnlySet<string> WriteInstructions(string projectDir, IReadOnlyList<Section> sections)
    {
        var path = Path.Combine(projectDir, InstructionsFileName);
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var added = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder(text);
        if (text.Length == 0)
        {
            sb.AppendLine("# 번역 지시 (요청마다 같은 부분)");
            sb.AppendLine();
            sb.AppendLine("request.md는 아래 절을 이름(`[S-…]`, `[R-…]`, `[J-…]`)으로 가리킨다. 절은 번역기 API 번역의 지시와 규칙 그대로이고,");
            sb.AppendLine("요청마다 다시 보내지 않으려고 여기 한 번만 둔다. 지시를 잊었으면 이 파일을 다시 읽는다.");
            sb.AppendLine("요청 줄의 `style: [ST-…]`는 그 이름의 절 본문이 그 줄의 style이라는 뜻이다.");
        }

        foreach (var section in sections)
        {
            if (text.Contains(Heading(section), StringComparison.Ordinal) || added.Contains(section.Id)) continue;
            sb.AppendLine();
            sb.AppendLine(Heading(section));
            sb.AppendLine();
            sb.AppendLine(section.Text);
            added.Add(section.Id);
        }

        if (added.Count > 0)
        {
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(path, sb.ToString());
        }

        return added;
    }

    /// <summary>The request as the model reads it: how to answer, the sections it uses, then its own text.</summary>
    public static string Render(AgentProject project, AgentRequest request, IReadOnlySet<string> newSections,
        IReadOnlyList<AgentRejection> rejections)
    {
        var parts = Split(request);
        var json = AgentAnswerCheck.IsJson(request);
        var repair = AgentAnswerCheck.IsRepair(request);
        var sb = new StringBuilder();
        sb.AppendLine($"# 번역 요청 ({Path.GetFileName(project.PluginPath)})");
        sb.AppendLine();
        sb.AppendLine($"- 요청 ID: {request.Hash} (시도 {request.Attempt + 1})");
        sb.AppendLine($"- 종류: {Kind(request, json, repair)}");
        sb.AppendLine($"- 글자 수: 이 파일 {CharsMark}자 (지시를 합친 요청 전체 {request.Chars:N0}자)");
        sb.AppendLine();
        sb.AppendLine("## 답하는 법");
        sb.AppendLine();
        sb.AppendLine("아래 '지시'와 '요청'을 번역기의 API 번역과 똑같이 따른다.");
        if (json)
        {
            sb.AppendLine("답은 JSON 하나만(`{\"translations\":[{\"id\":<요청의 id>,\"text\":\"<번역>\"}, ...]}`) 파일에 쓴다. 요청의 모든 id에 답하고,");
            sb.AppendLine("`__XT_...__` 같은 보호 토큰과 태그·줄바꿈은 그대로 둔다. 설명이나 코드 블록을 덧붙이지 않는다.");
            if (repair) sb.AppendLine("각 항목의 `current`(앞서 낸 번역)를 고친 번역을 `text`에 쓴다. 고칠 이유는 아래 '앞 답이 걸린 이유'에 있다.");
        }
        else if (repair)
        {
            sb.AppendLine("`<<<CURRENT`와 `CURRENT>>>` 사이의 번역(앞서 낸 답)을 고친 번역문만 파일에 쓴다. 원문(`<<<SOURCE`)은 옮길 대상이 아니라 참고다.");
            sb.AppendLine("끝 표시(`__XT_PH_9999__`)는 이 요청에 없으니 붙이지 않는다. 보호 토큰·태그·줄바꿈은 원문 그대로 두고, 설명이나 따옴표를 덧붙이지 않는다.");
        }
        else
        {
            sb.AppendLine("번역문만 파일에 쓴다. `<<<TEXT`와 `TEXT>>>` 사이의 글을 옮기고, `__XT_...__` 보호 토큰(끝의 `__XT_PH_9999__` 포함)과");
            sb.AppendLine("태그·줄바꿈은 그대로 둔다. 설명이나 따옴표를 덧붙이지 않는다.");
        }

        if (HasNumericPlaceholders(parts.UserPayload))
        {
            sb.AppendLine();
            sb.AppendLine("수치 자리표시자: `<mag>`·`__XT_PH_MAG_…__`·`__XT_PH_NUM_…__`는 수량 명사 앞에 둔다(`<mag>의 마법 피해를 줍니다`, `<mag>포인트`, `<mag>% 감소합니다`).");
            sb.AppendLine("바로 뒤에 시간 단위(초·분·시간·일·주·년·동안)나 그 글자로 시작하는 말(`피해를 <mag> 주고`의 '주고', '줍니다', '일격')이 오면 지속시간으로 읽혀 다시 요청된다.");
            sb.AppendLine("`<dur>`·`__XT_PH_DUR_…__`는 `<dur>초 동안`처럼 쓴다. 수치 토큰 바로 뒤에 을/를/에게 같은 조사를 붙이지 않는다.");
        }

        sb.AppendLine();
        sb.AppendLine($"쓴 뒤: `tullius submit {project.Key} <답 파일>` (답 파일은 매번 통째로 새로 쓴다)");

        if (rejections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 앞 답이 걸린 이유");
            sb.AppendLine();
            foreach (var rejection in rejections)
            {
                sb.AppendLine($"- {Label(rejection)}: {rejection.Reason}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 지시");
        sb.AppendLine();
        sb.AppendLine($"이 요청의 지시는 `{Path.Combine("work", project.Key, InstructionsFileName)}`의 아래 절이다. " +
                      "이 대화에서 아직 읽지 않은 절(새 절, 또는 대화를 새로 시작했으면 전부)은 먼저 읽고 답한다.");
        foreach (var section in parts.Sections)
        {
            var state = newSections.Contains(section.Id) ? "새 절" : "앞 요청과 같음";
            sb.AppendLine($"- [{section.Id}] {section.Title} ({state})");
        }

        if (parts.Sections.Any(section => section.Id.StartsWith("ST-", StringComparison.Ordinal)))
        {
            sb.AppendLine();
            sb.AppendLine("요청 줄의 `style: [ST-…]`는 그 이름의 '줄 style' 절 본문이 그 줄의 style이라는 뜻이다.");
        }

        sb.AppendLine();
        sb.AppendLine("## 요청");
        sb.AppendLine();
        sb.AppendLine(Listing(parts.UserPayload));
        var rendered = sb.ToString();
        return rendered.Replace(CharsMark, (rendered.Length - CharsMark.Length).ToString("N0"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The batch's Input JSON as the model reads it best: one line per item (<c>id rec edid | text</c>) and its notes
    /// (style, ctx, current) indented under it, the glossary on one line. Only the display: the request, its hash and
    /// what is replayed stay the run's JSON prompt, and the answer is the same JSON.
    /// </summary>
    internal static string Listing(string payload)
    {
        var lines = payload.Split('\n');
        var at = Array.FindIndex(lines, line => line.TrimEnd() == "Input JSON:");
        if (at < 0 || at + 1 >= lines.Length) return payload;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(lines[at + 1]);
        }
        catch (JsonException)
        {
            return payload;
        }

        if (root?["items"] is not JsonArray items) return payload;
        var sb = new StringBuilder();
        var languages = Value(root["source_language"]) is { } from && Value(root["target_language"]) is { } to ? $" ({from} → {to})" : "";
        sb.AppendLine($"줄 목록{languages}: 한 줄이 한 항목(`id rec edid | 원문`). 원문 안의 줄바꿈은 `\\n`으로 적었다.");
        sb.AppendLine("그 아래 들여쓴 줄은 그 항목의 `style`, `ctx`(앞뒤 줄, 참고만 하고 번역하지 않는다, 줄바꿈은 ` / `), `current`(고칠 앞 번역)다.");
        if (root["glossary"] is JsonArray glossary && glossary.Count > 0)
        {
            var pairs = glossary.OfType<JsonObject>().Select(g => $"{Value(g["source"])} → {Value(g["target"])}");
            sb.AppendLine("용어집: " + string.Join("; ", pairs));
        }

        sb.AppendLine();
        foreach (var item in items.OfType<JsonObject>())
        {
            var head = string.Join(" ", new[] { Value(item["id"]), Value(item["rec"]), Value(item["edid"]) }.Where(v => !string.IsNullOrEmpty(v)));
            sb.AppendLine($"{head} | {Escaped(Value(item["text"]) ?? Value(item["source"]) ?? "")}");
            foreach (var (name, node) in item)
            {
                if (name is "id" or "rec" or "edid" or "text" or "source" || Value(node) is not { Length: > 0 } value) continue;
                sb.AppendLine($"    {name}: {(name == "current" ? Escaped(value) : Flattened(value))}");
            }
        }

        var listed = lines[..at].Append(sb.ToString().TrimEnd()).Concat(lines[(at + 2)..]);
        return string.Join("\n", listed).Trim();
    }

    private static string? Value(JsonNode? node)
        => node is JsonValue value ? value.TryGetValue(out string? text) ? text : value.ToJsonString() : null;

    // As the answer's JSON string has it, so the model writes a line break back as \n.
    private static string Escaped(string text)
        => text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string Flattened(string text)
        => text.Replace(" (reference only):", ":").Replace("\r\n", " / ").Replace("\n", " / ");

    private const string CharsMark = "\u0001CHARS\u0001";

    private static string Kind(AgentRequest request, bool json, bool repair)
        => (json, repair) switch
        {
            (true, false) => "여러 줄 번역(JSON)",
            (true, true) => "여러 줄 고쳐 쓰기(JSON)",
            (false, true) => "한 줄 고쳐 쓰기(글)",
            _ => request.Purpose == "translate-chunk" ? "긴 글 한 조각 번역(글)" : "한 줄 번역(글)",
        };

    private static string Label(AgentRejection rejection)
    {
        var source = rejection.Source.Replace("\r\n", "⏎").Replace("\n", "⏎");
        if (source.Length > 60) source = source[..60] + "…";
        return rejection.Id is { } id ? $"id {id} ({source})" : $"이 줄 ({source})";
    }

    // The rules name <mag> and the tokens in every request; only the lines themselves tell whether they have one.
    private static bool HasNumericPlaceholders(string payload)
        => new[] { "<mag>", "<dur>", "<bur>" }.Any(tag => payload.Contains(tag, StringComparison.OrdinalIgnoreCase))
           || new[] { "__XT_PH_MAG_", "__XT_PH_NUM_", "__XT_PH_DUR_" }.Any(token => payload.Contains(token, StringComparison.Ordinal));

    private static string Heading(Section section) => $"## [{section.Id}] {section.Title}";

    private static string Id(string prefix, string text)
        => prefix + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim())))[..8].ToLowerInvariant();
}
