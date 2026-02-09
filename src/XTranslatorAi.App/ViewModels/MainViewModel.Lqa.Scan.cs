using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private async Task<List<LqaIssueViewModel>> BuildLqaIssuesAsync()
    {
        var ordered = Entries
            .Select(
                e =>
                    new LqaScanEntry(
                        Id: e.Id,
                        OrderIndex: e.OrderIndex,
                        Edid: e.Edid,
                        Rec: e.Rec,
                        Status: e.Status,
                        SourceText: e.SourceText ?? "",
                        DestText: e.DestText ?? ""
                    )
        )
            .ToList();

        var forceTokenGlossary = IsKoreanLanguage(TargetLang) ? BuildLqaForceTokenGlossary() : Array.Empty<GlossaryEntry>();
        var db = _projectState.Db;
        var tmFallbackNotes = db == null
            ? new Dictionary<long, string>()
            : await db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None);

        var issues = await LqaScanner.ScanAsync(
            entries: ordered,
            targetLang: TargetLang,
            forceTokenGlossary: forceTokenGlossary,
            onProgress: pct => StatusMessage = $"LQA: scanning... {pct}%",
            tmFallbackNotes: tmFallbackNotes
        );

        return issues
            .Select(
                i =>
                    new LqaIssueViewModel(
                        id: i.Id,
                        orderIndex: i.OrderIndex,
                        edid: i.Edid,
                        rec: i.Rec,
                        severity: i.Severity,
                        code: i.Code,
                        message: i.Message,
                        sourceText: i.SourceText,
                        destText: i.DestText
                    )
            )
            .ToList();
    }

    private IReadOnlyList<GlossaryEntry> BuildLqaForceTokenGlossary()
    {
        var project = Glossary
            .Select(ToCoreGlossaryEntry)
            .ToList();
        var global = GlobalGlossary.Count == 0
            ? null
            : GlobalGlossary.Select(ToCoreGlossaryEntry).ToList();

        var merged = GlossaryMerger.Merge(project, global);
        return merged
            .Where(e => e.Enabled)
            .Where(e => e.ForceMode == GlossaryForceMode.ForceToken)
            .Where(e => e.MatchMode != GlossaryMatchMode.Regex)
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceTerm) && !string.IsNullOrWhiteSpace(e.TargetTerm))
            .OrderByDescending(e => e.Priority)
            .ThenByDescending(e => e.SourceTerm.Length)
            .ToList();
    }

    private static GlossaryEntry ToCoreGlossaryEntry(GlossaryEntryViewModel vm)
    {
        return new GlossaryEntry(
            Id: vm.Id,
            Category: string.IsNullOrWhiteSpace(vm.Category) ? null : vm.Category.Trim(),
            SourceTerm: (vm.SourceTerm ?? "").Trim(),
            TargetTerm: (vm.TargetTerm ?? "").Trim(),
            Enabled: vm.Enabled,
            MatchMode: vm.MatchMode,
            ForceMode: vm.ForceMode,
            Priority: vm.Priority,
            Note: string.IsNullOrWhiteSpace(vm.Note) ? null : vm.Note.Trim()
        );
    }

    private static bool IsKoreanLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim();
        if (string.Equals(s, "korean", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ko", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.StartsWith("ko-", StringComparison.OrdinalIgnoreCase) || s.StartsWith("ko_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.Equals(s, "한국어", StringComparison.OrdinalIgnoreCase)
               || s.IndexOf("한국", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
