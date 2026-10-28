using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>The read-only commands an agent looks things up with: show, lookup and voice (the app may run).</summary>
public static partial class AgentCli
{
    // Lines by id (the id requests and check use), filtered: show <프로젝트> [--find 글자] [--rec WEAP:FULL] [--id 12,40]
    private static async Task ShowAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var rows = await c.RowsAsync(db);
        var ids = c.Ids?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : throw new AgentModeException($"--id에 숫자가 아닌 값: {s}")).ToHashSet();
        var picked = rows
            .Where(r => c.Rec == null || string.Equals(r.Rec, c.Rec, StringComparison.OrdinalIgnoreCase))
            .Where(r => ids == null || ids.Contains(r.Id))
            .Where(r => c.Find == null || (r.SourceText ?? "").Contains(c.Find, StringComparison.OrdinalIgnoreCase)
                                     || (r.DestText ?? "").Contains(c.Find, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.OrderIndex)
            .ToList();
        // A page: the rows after a row (--after <id>), --count of them, for reading a whole plugin in turn.
        var total = picked.Count;
        if (c.After != null)
        {
            var after = long.TryParse(c.After, out var a) ? a : throw new AgentModeException($"--after에 숫자가 아닌 값: {c.After}");
            var from = rows.FirstOrDefault(r => r.Id == after) ?? throw new AgentModeException($"없는 줄: id {after}");
            picked = picked.Where(r => r.OrderIndex > from.OrderIndex).ToList();
        }

        var left = picked.Count;
        if (c.Count != null)
        {
            var count = int.TryParse(c.Count, out var n) && n > 0 ? n : throw new AgentModeException($"--count는 1 이상의 숫자로 쓰세요: {c.Count}");
            picked = picked.Take(count).ToList();
        }

        Console.WriteLine($"줄: {picked.Count}" + (picked.Count != rows.Count ? $" / {rows.Count}" : "")
                          + (picked.Count < left ? $" (뒤에 {left - picked.Count}줄 더)" : "")
                          + (total != rows.Count && picked.Count != total ? $", 찾은 줄 {total}" : ""));
        // Who says each line, as the requests name it: the review needs it to judge a speech level, and had to read
        // the project DB for it in Meridia's Order.
        var speakers = picked.Count == 0 ? new Dictionary<long, string>()
            : await DialogueSpeakerNames.LoadAsync(db, await new GameStringService().TryLoadAsync(c.Ct), c.Ct);
        foreach (var r in picked)
        {
            Console.WriteLine($"#{r.Id} {r.Rec} [{StatusLabel(r.Status)}]");
            if (speakers.TryGetValue(r.Id, out var speaker))
            {
                var playerLine = string.Equals(r.Rec, "INFO:RNAM", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Rec, "DIAL:FULL", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine($"  화자: {(playerLine ? TranslationPrompt.PlayerSpeakerPrefix : "")}{speaker}");
            }

            Console.WriteLine($"  영어: {Flat(r.SourceText ?? "")}");
            Console.WriteLine($"  번역: {Flat(r.DestText ?? "")}");
        }

        if (picked.Count < left)
        {
            Console.WriteLine($"다음 쪽: --after {picked[^1].Id} --count {picked.Count}");
        }
    }

    // How the translator already writes an English name or term: lookup <프로젝트> <낱말> (read only, the app may run).
    private static async Task LookupAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var word = string.Join(" ", c.Rest.Skip(1)).Trim();
        if (word.Length == 0) throw new AgentModeException("찾을 영어 낱말을 지정하세요. (tullius.cmd lookup <프로젝트> <낱말>)");
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var global = await c.GlobalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, c.Ct);
        if (global == null)
        {
            // Without it "없습니다. 새 이름이면 네가 정하고" had the model invent a name the TM may have.
            Console.WriteLine("경고: 전체 DB를 열지 못해 전체 용어집·시리즈 TM은 찾지 못했습니다. 이름을 새로 정하기 전에 사용자에게 알리세요.");
        }

        var game = await new GameStringService().TryLoadAsync(c.Ct);
        var found = await AgentLookup.FindAsync(db, global, game, word, 12, c.Ct);
        Console.WriteLine($"찾은 낱말: {word}");
        if (found.Glossary.Count == 0 && found.Game == null && found.Memory.Count == 0 && found.ModTotal == 0)
        {
            Console.WriteLine("용어집·게임 사전·TM·이 모드의 번역에 없습니다. 새 이름이면 네가 정하고 앞뒤 요청과 같은 표기로 쓴다.");
            return;
        }

        foreach (var hit in found.Glossary) Console.WriteLine($"[{hit.Where}] {hit.Source} → {hit.Target}");
        if (found.Game != null) Console.WriteLine($"[게임 사전] {word} → {Flat(found.Game.Text)}");
        foreach (var hit in found.Memory) Console.WriteLine($"[TM] {Short(hit.Source, 120)} → {Short(hit.Target, 120)}");
        if (found.MemoryTotal > found.Memory.Count) Console.WriteLine($"(TM {found.MemoryTotal}건 중 짧은 {found.Memory.Count}건)");
        foreach (var hit in found.Mod ?? Array.Empty<AgentLookupHit>()) Console.WriteLine($"[이 모드에서 번역함] {Short(hit.Source, 120)} → {Short(hit.Target, 120)}");
        if (found.ModTotal > (found.Mod?.Count ?? 0)) Console.WriteLine($"(이 모드 {found.ModTotal}줄 중 짧은 {found.Mod!.Count}줄)");
    }

    // The names the run decided itself, for the closing report: names <프로젝트> (read only).
    private static async Task NamesAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var rows = await c.RowsAsync(db);
        var names = await AgentSession.NewNamesAsync(db, rows, c.Ct);
        if (names.Count == 0)
        {
            Console.WriteLine("새로 정한 이름 줄이 없습니다. 이름 줄은 모두 용어집·TM·게임 사전·한국어판에서 왔습니다.");
        }
        else
        {
            Console.WriteLine($"새로 정한 이름: {names.Count}개 (용어집·TM·게임 사전·한국어판에 없어 번역하며 정한 이름 줄, 앞말만 다른 같은 이름은 한 번)");
            foreach (var r in names) Console.WriteLine($"#{r.Id} {r.Rec}  {Flat(r.SourceText)} → {Flat(r.DestText)}");
        }

        // Names inside books and dialogue, which have no row of their own: the agent says how it wrote each.
        var global = await c.GlobalDb.GetOrCreateAsync(BethesdaFranchise.ElderScrolls, c.Ct);
        var inText = await AgentTextNames.FindAsync(db, global, await new GameStringService().TryLoadAsync(c.Ct), rows, c.Ct);
        if (inText.Count == 0) return;
        const int max = 80;
        Console.WriteLine($"본문 속 새 이름 후보: {inText.Count}개 (책·대사 같은 줄에 나온 영어 이름 가운데 용어집·TM·게임 사전·이 모드의 이름 줄에 없는 것. 각각 어떻게 옮겼는지 함께 보고한다)");
        foreach (var n in inText.Take(max))
        {
            Console.WriteLine($"- {n.Name}  " + string.Join(", ", n.Ids.Take(3).Select(id => "#" + id)) + (n.Ids.Count > 3 ? $" 외 {n.Ids.Count - 3}줄" : ""));
        }

        if (inText.Count > max) Console.WriteLine($"(그 밖에 {inText.Count - max}개)");
    }

    // How a base game NPC speaks in the Korean patch: voice <프로젝트> <영어 이름|한국어 이름|FormID> (read only). The
    // Teldryn trial's agent found Teldryn's 하오체 only by reading the lines the dictionary had filled.
    private static async Task VoiceAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        var query = string.Join(" ", c.Rest.Skip(1)).Trim();
        if (query.Length == 0) throw new AgentModeException("찾을 인물을 지정하세요. (tullius.cmd voice <프로젝트> <이름 또는 FormID>)");
        var game = await new GameStringService().TryLoadAsync(c.Ct);
        if (game?.Speakers is not { } index) throw new AgentModeException("게임 번역 사전에 화자 색인이 없습니다.");
        string FieldOf(string npc) => "NPC_/" + npc[..(npc.LastIndexOf(':') + 1)] + npc[(npc.LastIndexOf(':') + 1)..].ToUpperInvariant() + "/0/FULL/0";
        string? Korean(string npc) => game.FindRecord(FieldOf(npc), "")?.Korean;

        // A FormID as the request names it ("NPC 04038560") is in this plugin's master order.
        var found = index.FindNpcs(query).ToList();
        if (query.Length == 8 && query.All(Uri.IsHexDigit))
        {
            await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
            var plugin = await db.GetPluginMastersAsync(c.Ct);
            var formId = uint.Parse(query, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            if (plugin is { } p && formId >> 24 < p.Masters.Count)
            {
                var npc = $"{p.Masters[(int)(formId >> 24)].ToLowerInvariant()}:{formId & 0xFFFFFF:x6}";
                found = index.FindNpcs(npc).ToList();
            }
        }

        if (found.Count == 0)
        {
            found = index.Names.Keys.Where(npc => Korean(npc) is { } k && k.Contains(query, StringComparison.Ordinal))
                .Select(npc => (npc, index.Names[npc], index.LinesOf(npc).Count)).OrderByDescending(x => x.Item3).ToList();
        }

        if (found.Count == 0)
        {
            Console.WriteLine($"'{query}'인 바닐라 인물을 찾지 못했습니다(대사가 있는 NPC만 있습니다).");
            return;
        }

        if (found.Count > 1)
        {
            Console.WriteLine("찾은 인물: " + string.Join(", ", found.Take(6).Select(f => $"{f.Name}({Korean(f.Npc)}, {f.Npc}, {f.Lines}줄)")));
        }

        var (who, name, count) = found[0];
        var lines = index.LinesOf(who).Select(id => game.FindRecord(id, "")?.Korean).OfType<string>()
            .Select(Flat).Where(t => t.Length is >= 8 and <= 120).Distinct(StringComparer.Ordinal).ToList();
        Console.WriteLine($"{name} ({Korean(who) ?? "한국어 이름 없음"}, {who}): 한글패치 대사 {count}줄");
        // The level the requests name for this NPC when none is decided (Speaker: … (한글패치 말투: 하오체)).
        var all = index.LinesOf(who).Select(id => game.FindRecord(id, "")?.Korean).OfType<string>().ToList();
        Console.WriteLine("한글패치 말투: " + (DialogueSpeakerNames.KoreanPatchLevel(all) ?? "판단하기 어려움(대사가 적거나 섞임)"));
        var step = Math.Max(1, lines.Count / 15);
        foreach (var line in lines.Where((_, i) => i % step == 0).Take(15)) Console.WriteLine("- " + line);
    }
}
