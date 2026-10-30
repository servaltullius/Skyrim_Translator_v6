using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// A row as the project-wide quality check reads it. Every field is required: the GUI and the agent's check each built
/// their own input, and the agent's left out the linked earlier release (an English name that release kept was then
/// "left over") without anything noticing.
/// </summary>
/// <param name="PreviousText">The linked earlier release's translation of the field, or null.</param>
/// <param name="IsSourceFix">Marked "원문 오류 수정": translated against its English on purpose.</param>
public sealed record ProjectLqaRow(
    long Id, int OrderIndex, string? Edid, string? Rec, StringEntryStatus Status, string SourceText, string DestText,
    string? PreviousText, bool IsSourceFix);

/// <summary>
/// The quality check over a whole project, the same for the GUI and agent mode: they used to build its input apart and
/// drifted (the agent's lacked the earlier release, and stopped on an unreadable series TM the GUI went on without).
/// Each supplies its rows, glossary and series TM; the speakers, the TM-fallback notes and the decided speech levels are
/// read from the project DB here.
/// </summary>
public static class ProjectLqaScan
{
    /// <summary>
    /// An xTranslator XML opens its existing translations as 건너뜀 (Skipped); they are checked like finished rows. A row
    /// skipped on purpose keeps its source text and has nothing to check.
    /// </summary>
    public static StringEntryStatus CheckStatus(StringEntryStatus status, string? sourceText, string? destText)
        => status == StringEntryStatus.Skipped
           && !string.IsNullOrWhiteSpace(destText)
           && !string.Equals((destText ?? "").Trim(), (sourceText ?? "").Trim(), StringComparison.Ordinal)
            ? StringEntryStatus.Done
            : status;

    /// <summary>
    /// The glossary the check reads, the project's over the global ones, in the order the translation applies it: the
    /// forced terms it looks for (a regex term is not looked for, a pattern over-matches easily, but it takes its text
    /// first as in the translation: with "Dragon ?Priest" → 드래곤 프리스트, "Dragon" → 용 was reported missing from
    /// "드래곤 프리스트"), and the prompt-only word terms, the user's choice of a name for the TM name check.
    /// </summary>
    public static IReadOnlyList<GlossaryEntry> CheckGlossary(IReadOnlyList<GlossaryEntry> project, IReadOnlyList<GlossaryEntry>? global)
        => GlossaryMerger.Merge(project, global is { Count: > 0 } ? global : null)
            .Where(e => e.Enabled && (e.ForceMode == GlossaryForceMode.ForceToken
                                      ? e.MatchMode != GlossaryMatchMode.Regex || GlossaryApplier.IsValidRegexPattern(e.SourceTerm)
                                      : e.MatchMode != GlossaryMatchMode.Regex))
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceTerm) && !string.IsNullOrWhiteSpace(e.TargetTerm))
            .OrderByDescending(e => e.Priority)
            .ThenByDescending(e => e.SourceTerm.Length)
            .ToList();

    /// <summary>The check's glossary for the project's target: the term checks are Korean ones, and read none for another.</summary>
    public static IReadOnlyList<GlossaryEntry> CheckGlossary(string targetLang, IReadOnlyList<GlossaryEntry> project, IReadOnlyList<GlossaryEntry>? global)
        => LanguageHelper.IsKoreanLanguage(targetLang) ? CheckGlossary(project, global) : Array.Empty<GlossaryEntry>();

    /// <summary>
    /// The series TM the TM name index is built from, in the project's languages (the agent's check read english → korean
    /// whatever the project's were); null for a target that is not Korean or without a global DB. A read that fails throws:
    /// each caller goes on without it and says so its own way.
    /// </summary>
    public static async Task<IReadOnlyList<(string Source, string Target)>?> LoadReferenceMemoryAsync(
        ProjectDb? global, string sourceLang, string targetLang, CancellationToken ct)
    {
        if (global == null || !LanguageHelper.IsKoreanLanguage(targetLang)) return null;
        return (await global.GetTranslationMemoryEntriesAsync(sourceLang.Trim(), targetLang.Trim(), ct)).Select(e => (e.SourceText, e.DestText)).ToList();
    }

    /// <summary>A row of the check for a single re-checked row, without the project's speakers.</summary>
    public static LqaScanEntry ToEntry(ProjectLqaRow row, string? speaker = null)
        => new(row.Id, row.OrderIndex, row.Edid, row.Rec, CheckStatus(row.Status, row.SourceText, row.DestText), row.SourceText,
            row.DestText, row.PreviousText, speaker);

    /// <summary>Every row of the project as stored, with its earlier release's text and its 원문 오류 수정 mark.</summary>
    public static async Task<List<ProjectLqaRow>> LoadRowsAsync(ProjectDb db, CancellationToken ct)
    {
        var rows = await db.GetAllStringsAsync(ct);
        var previous = await db.GetPreviousTranslationsByStringIdAsync(ct);
        var sourceFix = await db.GetSourceFixIdsForCheckAsync(ct);
        return rows.Select(e => new ProjectLqaRow(e.Id, e.OrderIndex, e.Edid, e.Rec, e.Status, e.SourceText ?? "", e.DestText ?? "",
            previous.GetValueOrDefault(e.Id), sourceFix.Contains(e.Id))).ToList();
    }

    public sealed record Result(List<LqaIssue> Issues, ReferenceNameIndex? ReferenceNames);

    /// <param name="glossary">From <see cref="CheckGlossary"/>; empty for a target that is not Korean.</param>
    /// <param name="referenceMemory">The series TM, or null when it could not be read (the check goes on without its names).</param>
    public static async Task<Result> ScanAsync(
        ProjectDb? db, IReadOnlyList<ProjectLqaRow> rows, string targetLang, IReadOnlyList<GlossaryEntry> glossary,
        IReadOnlyList<(string Source, string Target)>? referenceMemory, Action<int>? onProgress, CancellationToken ct)
    {
        var speakers = db == null ? new Dictionary<long, string>() : await DialogueSpeakerNames.LoadCheckGroupsAsync(db, ct);
        var entries = rows.Select(row => ToEntry(row, speakers.GetValueOrDefault(row.Id))).ToList();
        var tmFallbackNotes = db == null ? new Dictionary<long, string>() : await db.GetStringNotesByKindAsync(TranslationConstants.TmFallbackNoteKind, ct);
        var speakerStyles = db == null ? null : await db.GetSpeakerStylesAsync(ct);
        var hasMemory = referenceMemory is { Count: > 0 };
        var notes = hasMemory ? LqaScanner.DropTmFallbacksMatchingMemory(tmFallbackNotes, entries, referenceMemory!) : tmFallbackNotes;
        var referenceNames = hasMemory ? ReferenceNameIndex.Build(referenceMemory!) : null;
        var issues = await LqaScanner.ScanAsync(entries, targetLang, glossary, onProgress, notes, referenceNames,
            rows.Where(row => row.IsSourceFix).Select(row => row.Id).ToHashSet(), speakerStyles);
        return new Result(issues, referenceNames);
    }
}
