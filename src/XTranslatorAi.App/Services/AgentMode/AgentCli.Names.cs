using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>
/// names --save: the names the run decided itself, once the user has looked the list over, go into the project glossary
/// as the terms command adds them. The closing report listed them and the user then wrote each one into a terms file.
/// </summary>
public static partial class AgentCli
{
    /// <summary>The note a saved name carries, so the GUI's 프로젝트 용어집 tab tells it from a term the user typed.</summary>
    internal const string SavedNameNote = "에이전트가 정한 이름";

    // names <프로젝트> --save [--id 12,40 | --except 12,40]
    private static async Task SaveNamesAsync(AgentCommand c)
    {
        if (!c.Save) throw new AgentModeException("이름을 용어집에 넣으려면 --save를 붙이세요(names <프로젝트> --save [--id 12,40 | --except 12,40]).");
        // "--id #12" in PowerShell is "--id" with the rest a comment: the option came without its list, and every name
        // went in where the user had chosen two.
        if (c.Rest.Contains("--id") || c.Rest.Contains("--except"))
        {
            throw new AgentModeException("--id·--except 뒤에 줄 번호가 없습니다. 번호는 # 없이 숫자만 쉼표로 쓰세요(--id 12,40). 아무것도 저장하지 않았습니다.");
        }

        if (c.Rest.Count > 1)
        {
            throw new AgentModeException($"알 수 없는 인자: {string.Join(" ", c.Rest.Skip(1))}. 번호는 # 없이 숫자만 쉼표로 붙여 쓰세요(--id 12,40). 아무것도 저장하지 않았습니다.");
        }

        var project = c.Session.Find(c.Arg(0, "프로젝트"));
        c.RefuseWhileAppRuns();
        await using var db = await ProjectDb.OpenOrCreateAsync(project.DbPath, c.Ct);
        var rows = await c.RowsAsync(db);
        var names = await AgentSession.NewNamesAsync(db, rows, c.Ct);
        var (lines, changed) = await SaveNewNamesAsync(db, names, rows, c.Ids, c.Except, c.Ct);
        foreach (var line in lines) Console.WriteLine(line);
        if (!changed) return;
        Console.WriteLine("넣은 이름은 다음 요청부터 용어집대로 쓰입니다. 같은 영어를 다르게 옮긴 줄이 있으면 check가 알려 줍니다.");
        await RemakeWaitingAsync(c, project, "바뀐 용어집으로", "옛 용어집으로");
    }

    /// <summary>
    /// Saves the new names (all, those in <paramref name="ids"/>, or all but those in <paramref name="except"/>) as forced
    /// terms, a name after a category ("Learning - Kaiten") as the name alone. Nothing is saved when an id is not on the
    /// list or one name has two translations among the chosen. A name the user switched off in the glossary is left as
    /// it is; one the mod's text also writes as a plain word ("Note" beside "a note") goes in as a hint only: forced, it
    /// would put the item's name in every sentence with the word.
    /// </summary>
    internal static async Task<(IReadOnlyList<string> Lines, bool Changed)> SaveNewNamesAsync(ProjectDb db, IReadOnlyList<StringEntry> names,
        IReadOnlyList<StringEntry> rows, string? ids, string? except, CancellationToken cancellationToken)
    {
        if (ids != null && except != null) throw new AgentModeException("--id와 --except는 함께 쓸 수 없습니다. 넣을 줄만(--id) 또는 뺄 줄만(--except) 주세요.");
        var listed = names.Select(n => n.Id).ToHashSet();
        var picked = Numbers(ids, "--id");
        var skipped = Numbers(except, "--except");
        var unknown = (picked ?? skipped ?? new List<long>()).Where(id => !listed.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new AgentModeException($"새로 정한 이름 목록에 없는 줄: {string.Join(", ", unknown.Select(id => "#" + id))}. names로 목록의 번호를 확인하세요"
                                         + "(terms로 먼저 넣은 이름은 목록에서 빠집니다). 아무것도 저장하지 않았습니다.");
        }

        if (names.Count == 0) return (new[] { "넣을 이름이 없습니다. 새로 정한 이름 줄이 없습니다(names로 확인)." }, false);
        var chosen = names.Where(n => picked?.Contains(n.Id) ?? !(skipped?.Contains(n.Id) ?? false))
            .Select(n => (Row: n, Part: AgentSession.NamePart(n.SourceText, n.DestText!)))
            .ToList();
        if (chosen.Count == 0) return (new[] { "넣을 이름이 없습니다(--except가 목록을 모두 뺐습니다)." }, false);

        // One name written two ways ("Kaiten" as 회천 and 카이텐, or behind two categories) is the user's to settle:
        // saving both kept the second.
        var split = chosen.GroupBy(n => n.Part.Source, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(n => n.Part.Target).Distinct(StringComparer.Ordinal).Count() > 1).ToList();
        if (split.Count > 0)
        {
            throw new AgentModeException("같은 이름을 다르게 옮긴 줄이 있습니다: "
                                         + string.Join("; ", split.Select(g => string.Join(" / ", g.Select(n => $"#{n.Row.Id} {Flat(n.Row.SourceText)} → {Flat(n.Row.DestText!)}"))))
                                         + ". 하나를 고르고 나머지는 --except로 빼거나 edit로 맞추세요. 아무것도 저장하지 않았습니다.");
        }

        // An entry the user switched off (in the GUI) is their choice over a global term: saving replaced it.
        var off = (await db.GetGlossaryAsync(cancellationToken)).Where(g => !g.Enabled)
            .Select(g => g.SourceTerm.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        var terms = new List<TermLine>();
        foreach (var name in chosen.GroupBy(n => n.Part.Source, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            if (off.Contains(name.Part.Source))
            {
                lines.Add($"건너뜀: {name.Part.Source} (용어집에 꺼 둔 항목이 있어 그대로 둠, 바꾸려면 terms로 정하세요)");
                continue;
            }

            terms.Add(new TermLine(name.Part.Source, name.Part.Target, SavedNameNote, Hint: AlsoAWord(name.Part.Source, name.Part.Target, rows), Remove: false));
        }

        if (terms.Count == 0) return (lines, false);
        var (saved, changed) = await SaveTermsAsync(db, terms, cancellationToken);
        lines.AddRange(saved);
        var hints = terms.Where(t => t.Hint).Select(t => t.Source!).ToList();
        if (hints.Count > 0) lines.Add($"참고만으로 넣은 이름: {string.Join(", ", hints)} (이 모드 글에 보통 낱말로도 나와, 강제하면 문장 속 낱말까지 이름으로 바뀝니다)");
        return (lines, changed);
    }

    /// <summary>
    /// Whether the mod's text other than its name lines also has the name as a plain word, as the glossary would find
    /// it (whole words, the plural too, case ignored): written in other capitals ("a note", "iron daggers"), or at all
    /// when the name is lowercase itself.
    /// </summary>
    internal static bool AlsoAWord(string source, string target, IReadOnlyList<StringEntry> rows)
    {
        if (!source.Any(char.IsLetter)) return false;
        var matcher = GlossaryApplier.MatcherFor(new GlossaryEntry(0, null, source, target, true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null));
        if (matcher == null) return false;
        var lowercase = !source.Any(char.IsUpper);
        var stem = source.EndsWith('s') && source.Length > 1 ? source[..^1] : source;
        foreach (var r in rows)
        {
            if (TranslationRowRules.IsNameField(r.Rec) || r.SourceText is not { } text || text.IndexOf(stem, StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (System.Text.RegularExpressions.Match m in matcher.Matches(text))
            {
                if (lowercase || string.CompareOrdinal(m.Value, 0, source, 0, Math.Min(m.Value.Length, source.Length)) != 0) return true;
            }
        }

        return false;
    }

    private static List<long>? Numbers(string? list, string option)
    {
        if (list == null) return null;
        var numbers = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s.TrimStart('#'), out var v) ? v : throw new AgentModeException($"{option}에 숫자가 아닌 값: {s}"))
            .Distinct().ToList();
        return numbers.Count > 0 ? numbers : throw new AgentModeException($"{option}에 줄 번호가 없습니다. {option} 12,40처럼 지정하세요.");
    }
}
