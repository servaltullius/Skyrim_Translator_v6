using System;

namespace XTranslatorAi.App.ViewModels;

public sealed class LqaIssueViewModel
{
    public long Id { get; }
    public int OrderIndex { get; }
    public string? Edid { get; }
    public string? Rec { get; }

    public string Severity { get; }
    public string Code { get; }
    public string Message { get; }

    public string SourcePreview { get; }
    public string DestPreview { get; }

    public LqaIssueViewModel(
        long id,
        int orderIndex,
        string? edid,
        string? rec,
        string severity,
        string code,
        string message,
        string sourceText,
        string destText
    )
    {
        Id = id;
        OrderIndex = orderIndex;
        Edid = edid;
        Rec = rec;
        Severity = severity ?? "";
        Code = code ?? "";
        Message = message ?? "";
        SourcePreview = Preview(sourceText);
        DestPreview = Preview(destText);
    }

    public bool MatchesQuery(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return ContainsIgnoreCase(OrderIndex.ToString(), q)
               || ContainsIgnoreCase(Id.ToString(), q)
               || ContainsIgnoreCase(Edid ?? "", q)
               || ContainsIgnoreCase(Rec ?? "", q)
               || ContainsIgnoreCase(Severity, q)
               || ContainsIgnoreCase(Code, q)
               || ContainsIgnoreCase(Message, q)
               || ContainsIgnoreCase(SourcePreview, q)
               || ContainsIgnoreCase(DestPreview, q);
    }

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var s = text.Replace("\r", "").Replace("\n", " ");
        return s.Length <= 120 ? s : s[..120] + "…";
    }
}

