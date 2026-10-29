using System;
using System.Linq;
using System.Threading.Tasks;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>The hold command (AgentSession.Hold): lines the agent app's model will not translate, set aside.</summary>
public static partial class AgentCli
{
    // hold <프로젝트> lists the held lines; hold <프로젝트> --id 12,40 [--note 이유] holds them; with --undo they wait again.
    private static async Task HoldAsync(AgentCommand c)
    {
        var project = c.Session.Find(c.Arg(0, "프로젝트")) with { Lane = c.Lane };
        if (c.Ids == null)
        {
            // "hold <키> --note 이유" or "--undo" with the ids left out showed the list, as if it had done something.
            if (c.Note != null || c.Undo) throw new AgentModeException($"보류할(풀) 줄을 --id 12,40처럼 지정하세요. 목록은 hold {project.Key}만 쓰면 나옵니다.");
            var held = await c.Session.HeldAsync(project, c.Ct);
            if (held.Count == 0)
            {
                Console.WriteLine($"보류한 줄: 없음. 번역할 수 없는 줄은 hold {project.Key} --id 12,40 --note \"이유\"로 보류합니다.");
                return;
            }

            Console.WriteLine($"보류한 줄: {held.Count} (번역하지 않음, export 때 영어 그대로 저장)");
            foreach (var line in held) Console.WriteLine($"- #{line.Row.Id} {line.Row.Rec} | {Short(line.Row.SourceText)} | {Flat(line.Label)}");
            return;
        }

        c.RefuseWhileAppRuns();
        var ids = c.Ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var v) ? v : throw new AgentModeException($"--id에 숫자가 아닌 값: {s}")).Distinct().ToList();
        if (ids.Count == 0) throw new AgentModeException("--id에 줄 번호가 없습니다. --id 12,40처럼 지정하세요.");
        if (c.Undo)
        {
            var released = await c.Session.ReleaseAsync(project, ids, c.Ct);
            Console.WriteLine($"보류를 풀었습니다: {released.Count}줄" + (released.Count < ids.Count ? $" (보류가 아니던 줄 {ids.Count - released.Count}은 그대로)" : "") + ".");
            if (released.Count > 0) await RemakeAfterHoldAsync(c, project, "보류를 푼 대로");
            return;
        }

        var result = await c.Session.HoldAsync(project, ids, c.Note, c.Ct);
        foreach (var line in result.Held) Console.WriteLine($"#{line.Row.Id} {Short(line.Row.SourceText)}: {Flat(line.Label)}");
        Console.WriteLine($"보류한 줄: {result.Held.Count}. 번역하지 않고 export 때 영어 그대로 저장합니다. "
                          + $"끝 보고에 hold {project.Key}{LaneOption(project)}의 목록을 {Whom(project)} 알리세요.");
        if (result.SameEnglishWaiting.Count > 0)
        {
            Console.WriteLine($"같은 영어의 남은 줄: {string.Join(", ", result.SameEnglishWaiting.Select(id => "#" + id))}. 보류하지 않았습니다. "
                              + "다른 장면이나 다른 종류의 이름이면 번역될 수 있으니, 요청에 다시 나와 같은 이유로 번역할 수 없을 때만 보류하세요.");
        }

        await RemakeAfterHoldAsync(c, project, "보류한 줄을 빼고");
    }

    /// <summary>
    /// The waiting request made again. Only the request that held the lines changes: the context command's word to the
    /// lanes ("open으로 다시 열어 대기 요청을 지운 뒤") had a lane's agent, or the parent, wipe every lane's request.
    /// </summary>
    private static async Task RemakeAfterHoldAsync(AgentCommand c, AgentProject project, string made)
    {
        var (remade, lanesWaiting, left) = await c.Session.RemakePendingAsync(project, c.Ct);
        var lanesBusy = project.Lane == null && lanesWaiting > 0;
        if (c.Session.SeriesTmNote is { } tmNote) Console.WriteLine(tmNote);
        if (remade is { Next: null, Problem: null, PendingAfter: 0 })
        {
            // Holding the last lines left finishes the run: it said "줄은 모두 번역돼", of lines held untranslated.
            Console.WriteLine($"남은 줄이 없습니다(번역 완료). 다음: tullius.cmd check {project.Key}, tullius.cmd export {project.Key}");
            if (remade.MissingGlobalData.Count > 0)
            {
                Console.WriteLine($"경고: 전체 DB의 {string.Join("·", remade.MissingGlobalData)}을(를) 읽지 못했습니다(용어 고정·한글패치 이름이 빠졌을 수 있음). 사용자에게 알리세요.");
            }
        }
        else if (DescribeRemade(remade, made) is { } note)
        {
            Console.WriteLine(note);
        }
        else if (remade == null && !left && !lanesBusy && c.Session.Pending(project) == null)
        {
            Console.WriteLine($"대기 중인 요청이 없습니다. tullius.cmd next {project.Key}{LaneOption(project)}로 이어 가세요.");
        }

        if (left)
        {
            Console.WriteLine("알림: 다른 대기 요청이 있어 이 요청을 다시 만들지 못했습니다. 보류를 바꾼 줄이 그 요청에 있으면, 답을 낼 때 요청이 다시 만들어집니다(그 답은 버려질 수 있음).");
        }

        if (lanesBusy)
        {
            Console.WriteLine($"알림: 갈래 {lanesWaiting}개에 대기 중인 요청이 있습니다. 보류를 바꾼 줄이 든 갈래는 다음 submit 때 요청이 다시 만들어지고, "
                              + "그 갈래가 앞 요청에 쓴 답은 버려질 수 있습니다(open으로 다시 열 필요 없음). 갈래 없는 next는 갈래가 모두 끝난 뒤에 합니다.");
        }
    }

    /// <summary>A line saying how many lines are held, for status and the end of the run; null when none.</summary>
    private static async Task<string?> HeldNoteAsync(AgentCommand c, AgentProject project)
    {
        var held = (await c.Session.HeldAsync(project, c.Ct)).Count;
        return held == 0 ? null : $"보류한 줄: {held} (번역하지 않음, export 때 영어 그대로). 목록: tullius.cmd hold {project.Key}{LaneOption(project)}";
    }

    private static string LaneOption(AgentProject project) => project.Lane is { } own ? $" --lane {own}" : "";

    // A lane's agent reports to the agent that started it, not to the user (guide-lanes.md).
    private static string Whom(AgentProject project) => project.Lane == null ? "사용자에게" : "부모 에이전트에게";
}
