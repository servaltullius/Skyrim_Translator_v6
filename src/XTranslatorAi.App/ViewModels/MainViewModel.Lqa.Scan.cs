using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;
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
                        Status: LqaStatus(e),
                        SourceText: e.SourceText ?? "",
                        DestText: e.DestText ?? "",
                        PreviousText: e.PreviousTranslation
                    )
        )
            .ToList();

        var forceTokenGlossary = LanguageHelper.IsKoreanLanguage(TargetLang) ? BuildLqaForceTokenGlossary() : Array.Empty<GlossaryEntry>();
        var db = _projectState.Db;
        var tmFallbackNotes = db == null
            ? new Dictionary<long, string>()
            : await db.GetStringNotesByKindAsync("tm_fallback", CancellationToken.None);

        // The rules read only this snapshot, so they run on a worker thread. On the UI thread the
        // project-wide rules (name consistency, tone majorities) ran before the first progress report
        // and froze the window for seconds on large projects. Progress is posted back to the UI thread.
        // The series TM holds tens of thousands of entries, and SQLite's async calls complete synchronously.
        var referenceMemory = await Task.Run(TryLoadLqaReferenceMemoryAsync);
        var targetLang = TargetLang;
        IProgress<int> progress = new Progress<int>(pct => StatusMessage = $"품질 검사 중... {pct}%");
        var notes = referenceMemory is { Count: > 0 }
            ? LqaScanner.DropTmFallbacksMatchingMemory(tmFallbackNotes, ordered, referenceMemory)
            : tmFallbackNotes;
        var issues = await Task.Run(() => LqaScanner.ScanAsync(
            entries: ordered,
            targetLang: targetLang,
            forceTokenGlossary: forceTokenGlossary,
            onProgress: progress.Report,
            tmFallbackNotes: notes,
            referenceNames: referenceMemory is { Count: > 0 } ? XTranslatorAi.Core.Translation.ReferenceNameIndex.Build(referenceMemory) : null
        ));

        return issues.Select(ToIssueViewModel).ToList();
    }

    /// <summary>The series TM the translation's official-name index is built from; null when it cannot be read.</summary>
    private async Task<IReadOnlyList<(string Source, string Target)>?> TryLoadLqaReferenceMemoryAsync()
    {
        if (!LanguageHelper.IsKoreanLanguage(TargetLang))
        {
            return null;
        }

        try
        {
            var globalDb = await _globalProjectDbService.GetOrCreateAsync(CancellationToken.None);
            if (globalDb == null)
            {
                return null;
            }

            var entries = await globalDb.GetTranslationMemoryEntriesAsync(SourceLang.Trim(), TargetLang.Trim(), CancellationToken.None);
            return entries.Select(e => (e.SourceText, e.DestText)).ToList();
        }
        catch (Exception ex)
        {
            XTranslatorAi.App.Services.AppLog.Write($"WARN 품질 검사용 공식 이름 색인을 읽지 못했습니다: {ex.Message}");
            return null;
        }
    }

    private static LqaIssueViewModel ToIssueViewModel(LqaIssue i)
        => new(id: i.Id, orderIndex: i.OrderIndex, edid: i.Edid, rec: i.Rec, severity: i.Severity, code: i.Code,
            message: i.Message, sourceText: i.SourceText, destText: i.DestText);

    // Codes that compare a row with the rest of the project; a re-check of one row cannot judge them.
    private static readonly HashSet<string> ProjectWideLqaCodes = new(StringComparer.Ordinal)
    {
        "name_inconsistent", "tone_inconsistent", "tone_differs_from_plugin", "rec_tone", "same_source_variant", "prefixed_name_variant", "tm_fallback", "official_name_missing",
    };

    /// <summary>
    /// After a row is saved, its quality-check results are recomputed from the new text. The list kept showing the
    /// old translation and its problems until the next full scan, so working through it meant guessing which rows
    /// were already fixed. Checks that compare rows across the project stay until the next full scan.
    /// </summary>
    private async Task RecheckLqaRowAsync(StringEntryViewModel entry)
    {
        if (IsLqaScanning || !LqaIssues.Any(i => i.Id == entry.Id))
        {
            return;
        }

        var db = _projectState.Db;
        var scanEntry = new LqaScanEntry(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, LqaStatus(entry),
            entry.SourceText ?? "", entry.DestText ?? "", entry.PreviousTranslation);
        var glossary = LanguageHelper.IsKoreanLanguage(TargetLang) ? BuildLqaForceTokenGlossary() : Array.Empty<GlossaryEntry>();
        var targetLang = TargetLang;
        var fresh = await Task.Run(() => LqaScanner.ScanAsync(new[] { scanEntry }, targetLang, glossary));
        if (!ReferenceEquals(db, _projectState.Db))
        {
            return;
        }

        var kept = LqaIssues.Where(i => i.Id != entry.Id || ProjectWideLqaCodes.Contains(i.Code)).ToList();
        var replacements = fresh.Where(i => !ProjectWideLqaCodes.Contains(i.Code)).Select(ToIssueViewModel);
        var selected = SelectedLqaIssue;
        // The order of the full scan: errors, warnings, information, each by row.
        LqaIssues.ReplaceAll(kept.Concat(replacements)
            .OrderBy(i => LqaSeverityWeight(i.Severity)).ThenBy(i => i.OrderIndex).ThenBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
            .ToList());
        LqaIssuesView.Refresh();
        if (selected != null && LqaIssues.Contains(selected))
        {
            SelectedLqaIssue = selected;
        }

        ClearLqaCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// An xTranslator XML opens its existing translations as 건너뜀 (Skipped); they are checked like finished rows. A row
    /// skipped on purpose keeps its source text and has nothing to check.
    /// </summary>
    private static StringEntryStatus LqaStatus(StringEntryViewModel entry)
        => entry.Status == StringEntryStatus.Skipped
           && !string.IsNullOrWhiteSpace(entry.DestText)
           && !string.Equals((entry.DestText ?? "").Trim(), (entry.SourceText ?? "").Trim(), StringComparison.Ordinal)
            ? StringEntryStatus.Done
            : entry.Status;

    private static int LqaSeverityWeight(string severity)
        => string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase) ? 0
            : string.Equals(severity, "Warn", StringComparison.OrdinalIgnoreCase) ? 1
            : 2;

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

}
