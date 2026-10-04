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
                        Status: e.Status,
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
        var referenceMemory = await TryLoadLqaReferenceMemoryAsync();
        var targetLang = TargetLang;
        IProgress<int> progress = new Progress<int>(pct => StatusMessage = $"품질 검사 중... {pct}%");
        var issues = await Task.Run(() => LqaScanner.ScanAsync(
            entries: ordered,
            targetLang: targetLang,
            forceTokenGlossary: forceTokenGlossary,
            onProgress: progress.Report,
            tmFallbackNotes: tmFallbackNotes,
            referenceNames: referenceMemory is { Count: > 0 } ? XTranslatorAi.Core.Translation.ReferenceNameIndex.Build(referenceMemory) : null
        ));

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
