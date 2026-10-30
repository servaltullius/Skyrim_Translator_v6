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

/// <summary>The style command: the speech levels decided per speaker and listener.</summary>
public static partial class AgentCli
{
    // The speech levels decided for the project: style <프로젝트> lists them and the speakers still undecided;
    // style <프로젝트> <화자> <말투> [--to <상대>] [--note <메모>] decides one, --clear takes it back. Requests name a
    // decided level next to the speaker, and check compares lines with it instead of with the speaker's other lines.
    private static async Task StyleAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var values = await DialogueSpeakerNames.LoadFormIdsAsync(db, c.Ct);
        var game = await c.GameStrings.TryLoadAsync(c.Ct);
        var labels = await DialogueSpeakerNames.LoadAsync(db, game, c.Ct);
        var masters = await db.GetPluginMastersAsync(c.Ct);
        static string Bare(string label)
        {
            foreach (var cut in new[] { DialogueSpeakerNames.GuessedListenerLabelSeparator, DialogueSpeakerNames.ListenerLabelSeparator, " (정한 말투", DialogueSpeakerNames.PatchStyleLabel, DialogueSpeakerNames.MaleLabel, DialogueSpeakerNames.FemaleLabel })
            {
                var at = label.IndexOf(cut, StringComparison.Ordinal);
                if (at >= 0) label = label[..at];
            }

            return label;
        }

        // Each NPC speaker of the project by FormID, with its name and line count.
        var npcs = values.Where(v => labels.ContainsKey(v.Key))
            .GroupBy(v => v.Value.Split(DialogueSpeakerNames.ListenerSeparator)[0])
            .ToDictionary(g => g.Key, g => (Name: Bare(labels[g.First().Key]), Lines: g.Count()), StringComparer.Ordinal);
        string NameOf(string who) => who == SpeakerStyles.PlayerSpeaker ? "플레이어"
            : who == SpeakerStyles.Player ? "플레이어"
            : who == SpeakerStyles.Other ? "다른 인물"
            : who == SpeakerStyles.Anyone ? "누구에게나"
            : npcs.TryGetValue(who, out var npc) ? $"{npc.Name} ({who})"
            : who.StartsWith('@') ? $"{who} (마스터가 빠져 쓰이지 않음, 돌아오면 다시 쓰임)" : who;
        string Resolve(string query, bool listener)
        {
            var q = query.Trim();
            // Copied from the list as the guide says, with what follows the name: "Alara(01001234, 12줄)", "@Dawnguard.esm|
            // 01001234 (마스터가 빠져 쓰이지 않음, …)". Taken whole, it was no speaker and clearing failed every time. A name
            // with its own parentheses ("Alara (Ghost)") stays as it is.
            if (q.EndsWith(')') && q.LastIndexOf('(') is var open and > 0)
            {
                var inside = q[(open + 1)..^1].Split(',')[0].Trim();
                if (inside.Length == 8 && inside.All(Uri.IsHexDigit))
                {
                    return inside.ToUpperInvariant();
                }

                if (q.StartsWith('@'))
                {
                    q = q[..open].TrimEnd();
                }
            }

            if (q is "player" or "플레이어") return listener ? SpeakerStyles.Player : SpeakerStyles.PlayerSpeaker;
            if (listener && q is "other" or "다른 인물" or "다른인물") return SpeakerStyles.Other;
            if (listener && q is "*" or "anyone" or "누구에게나" or "모두") return SpeakerStyles.Anyone;
            if (q.Length == 8 && q.All(Uri.IsHexDigit)) return q.ToUpperInvariant();
            // A style kept for an NPC whose master left the list ("@Dawnguard.esm|01001234"), to clear it.
            if (q.StartsWith('@') && q.Contains('|')) return q;
            var found = npcs.Where(n => n.Value.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            // A base game NPC's label is its Korean name: its English name is in the dictionary's speaker index.
            if (found.Count == 0 && game?.Speakers is { } index && masters is { } m)
            {
                var known = index.FindNpcs(q).Select(n =>
                {
                    var owner = n.Npc[..n.Npc.LastIndexOf(':')];
                    var at = m.Masters.ToList().FindIndex(x => string.Equals(x, owner, StringComparison.OrdinalIgnoreCase));
                    return at < 0 ? null : $"{at:X2}{n.Npc[(n.Npc.LastIndexOf(':') + 1)..].ToUpperInvariant()}";
                }).OfType<string>().ToHashSet(StringComparer.Ordinal);
                found = npcs.Where(n => known.Contains(n.Key)).ToList();
            }
            return found.Count switch
            {
                1 => found[0].Key,
                0 => throw new AgentModeException($"'{q}'인 화자가 이 프로젝트에 없습니다. FormID로 지정하세요."),
                _ => throw new AgentModeException($"'{q}'에 맞는 화자가 여럿입니다: {string.Join(", ", found.Take(6).Select(f => $"{f.Value.Name}({f.Key})"))}. FormID로 지정하세요."),
            };
        }

        if (c.Rest.Count <= 1)
        {
            var styles = await db.GetSpeakerStylesAsync(c.Ct);
            Console.WriteLine(styles.Count == 0 ? "정한 말투: 없음" : $"정한 말투: {styles.Count}");
            foreach (var style in styles)
            {
                Console.WriteLine($"- {NameOf(style.Speaker)} → {NameOf(style.Listener)}: {style.Level}" + (style.Note is { Length: > 0 } note ? $" ({note})" : ""));
            }

            // Every speaker with a few lines, and the NPCs the player speaks to: lanes translate at the same time and do
            // not see each other's choices (in Meridia's Order three lanes gave Findar 하게체 and 반말, and the player
            // 해요체 and 반말 to Velteth), so the parent decides them before the lanes start.
            var undecided = npcs.Where(n => styles.All(s => s.Speaker != n.Key) && n.Value.Lines >= 5)
                .OrderByDescending(n => n.Value.Lines).Take(30).ToList();
            if (undecided.Count > 0)
            {
                Console.WriteLine("말투를 정하지 않은 화자: " + string.Join(", ", undecided.Select(n => $"{n.Value.Name}({n.Key}, {n.Value.Lines}줄)")));
            }

            var playerRows = (await c.RowsAsync(db))
                .Where(r => TranslationRowRules.IsPlayerLine(r.Rec))
                .Select(r => values.TryGetValue(r.Id, out var v) ? v.Split(DialogueSpeakerNames.ListenerSeparator)[0] : null)
                .OfType<string>()
                .GroupBy(npc => npc)
                .Where(g => npcs.ContainsKey(g.Key) && styles.All(s => s.Speaker != SpeakerStyles.PlayerSpeaker || s.Listener != g.Key))
                .OrderByDescending(g => g.Count()).Take(20).ToList();
            // An NPC's lines to another NPC (a scene's listener): the lanes gave Alara 반말 and 해요체 to Vaegra.
            var npcPairs = values.Values
                .Where(v => v.Contains(DialogueSpeakerNames.ListenerSeparator) && !v.EndsWith(DialogueSpeakerNames.GuessedListenerMark))
                .Select(v => v.Split(DialogueSpeakerNames.ListenerSeparator))
                .Where(p => p.Length == 2 && p[1] != SpeakerStyles.Other && npcs.ContainsKey(p[0]) && npcs.ContainsKey(p[1]))
                .GroupBy(p => (Speaker: p[0], Listener: p[1]))
                .Where(g => g.Count() >= 3 && styles.All(s => s.Speaker != g.Key.Speaker || s.Listener != g.Key.Listener && s.Listener != SpeakerStyles.Anyone))
                .OrderByDescending(g => g.Count()).Take(20).ToList();
            if (npcPairs.Count > 0)
            {
                Console.WriteLine("다른 인물에게 하는 말투를 정하지 않은 쌍: " + string.Join(", ", npcPairs.Select(g =>
                                      $"{npcs[g.Key.Speaker].Name} → {npcs[g.Key.Listener].Name}({g.Key.Speaker}>{g.Key.Listener}, {g.Count()}줄)"))
                                  + $" → style {project.Key} <화자> <말투> --to <상대>");
            }

            if (playerRows.Count > 0)
            {
                Console.WriteLine("플레이어 말투를 정하지 않은 상대: " + string.Join(", ", playerRows.Select(g => $"{npcs[g.Key].Name}({g.Key}, {g.Count()}줄)"))
                                  + $" → style {project.Key} player <말투> --to <상대>");
            }

            return;
        }

        c.RefuseWhileAppRuns();
        var speaker = Resolve(c.Arg(1, "화자"), listener: false);
        var listenerKey = c.To == null ? SpeakerStyles.Player : Resolve(c.To, listener: true);
        var level = "";
        if (!c.Clear)
        {
            level = SpeakerStyles.Normalize(c.Arg(2, "말투")) ?? throw new AgentModeException(
                $"말투는 {string.Join(", ", SpeakerStyles.Levels)} 중 하나로 쓰세요.");
        }

        // Clearing a pair that has no style printed 지움 all the same (a listener left out, a name copied with its note).
        if (!await db.SetSpeakerStyleAsync(new SpeakerStyle(speaker, listenerKey, level, c.Note), c.Ct))
        {
            throw new AgentModeException($"지울 말투가 없습니다: {NameOf(speaker)} → {NameOf(listenerKey)}. "
                                         + $"style {project.Key}로 목록을 보고 화자와 상대(--to)를 목록 그대로 쓰세요.");
        }

        Console.WriteLine(c.Clear
            ? $"지움: {NameOf(speaker)} → {NameOf(listenerKey)}"
            : $"정함: {NameOf(speaker)} → {NameOf(listenerKey)}: {level}");
        await RemakeWaitingAsync(c, project, "정한 말투로", "옛 말투로");
    }
}
