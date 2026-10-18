using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>A line the run did not save from an answer, and why (in words for the model that wrote it).</summary>
/// <param name="Id">The row id the request gave the line (null for a one-line text request, which has none).</param>
public sealed record AgentRejection(long? Id, string Source, string Reason);

/// <summary>
/// Reads a request's lines and checks an answer to them the way the run's batch result check does, so a line the run
/// asks for again can be told apart with its reason. It decides nothing: whether a line was saved is the run's.
/// </summary>
public static class AgentAnswerCheck
{
    /// <summary>Lines of a request: the batch items (id, text) or the one text of a text request (id null).</summary>
    public static IReadOnlyList<(long? Id, string Text)> Lines(AgentRequest request)
    {
        var user = string.Join("\n", request.Messages.Where(m => m.Role == "user").Select(m => m.Text));
        if (IsJson(request))
        {
            var start = user.IndexOf("Input JSON:", StringComparison.Ordinal);
            var json = start < 0 ? -1 : user.IndexOf('{', start);
            if (json < 0) return Array.Empty<(long?, string)>();
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(user[json..]));
                using var payload = JsonDocument.ParseValue(ref reader);
                if (!payload.RootElement.TryGetProperty("items", out var items)) return Array.Empty<(long?, string)>();
                // A translate batch gives "text"; a repair batch gives "source" (and "current", the earlier answer).
                return items.EnumerateArray()
                    .Select(item => ((long?)item.GetProperty("id").GetInt64(), StringProperty(item, "text") ?? StringProperty(item, "source") ?? ""))
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<(long?, string)>();
            }
        }

        var text = Between(user, "<<<TEXT", "TEXT>>>") ?? Between(user, "<<<SOURCE", "SOURCE>>>");
        return text == null ? Array.Empty<(long?, string)>() : new[] { ((long?)null, text) };
    }

    /// <summary>The lines of <paramref name="request"/> the answer gets wrong, with the reason; missing ids included.</summary>
    public static IReadOnlyDictionary<long, string> Explain(AgentRequest request, string answer, string targetLang,
        PlaceholderSemanticRepairMode mode, out string? textReason)
    {
        textReason = null;
        var reasons = new Dictionary<long, string>();
        var lines = Lines(request);
        if (!IsJson(request))
        {
            if (lines.Count == 1) textReason = TranslationAnswerDiagnostics.Explain(lines[0].Text, answer, targetLang, mode);
            return reasons;
        }

        IReadOnlyDictionary<long, string> answered;
        try
        {
            answered = TranslationResultParser.ParseTranslations(answer);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return reasons;
        }

        foreach (var (id, text) in lines)
        {
            if (id is not { } key) continue;
            if (!answered.TryGetValue(key, out var translation))
            {
                reasons[key] = "답에 이 id가 없습니다.";
                continue;
            }

            if (TranslationAnswerDiagnostics.Explain(text, translation, targetLang, mode) is { } reason) reasons[key] = reason;
        }

        return reasons;
    }

    /// <summary>The ids an answer gives, or null when it is not a translations JSON.</summary>
    public static IReadOnlyCollection<long>? AnsweredIds(string answer)
    {
        try
        {
            return TranslationResultParser.ParseTranslations(answer).Keys.ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool IsJson(AgentRequest request) => string.Equals(request.ResponseMimeType, "application/json", StringComparison.OrdinalIgnoreCase);

    /// <summary>A request that asks to fix an earlier answer (repair) rather than to translate.</summary>
    public static bool IsRepair(AgentRequest request) => request.Purpose?.StartsWith("repair", StringComparison.Ordinal) == true;

    private static string? StringProperty(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Between(string text, string open, string close)
    {
        var from = text.IndexOf(open, StringComparison.Ordinal);
        var to = text.LastIndexOf(close, StringComparison.Ordinal);
        return from < 0 || to < from ? null : text[(from + open.Length)..to].Trim('\r', '\n');
    }
}
