using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Xml;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    public string CurrentXmlFileName
        => _projectState.CurrentXmlFileName;

    // Opening a file replaces the project, which stops a running translation. XML 열기/ESP 열기 and window drops
    // stayed available during a run and stopped it without asking; a plugin dropped on "이전 번역 참고" (which
    // refuses to link while translating) even fell through to the window and was opened as the project.
    // Opening now waits until the run is stopped or finished.
    private bool CanOpenProject() => IsWorkspaceInteractive && !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanOpenProject))]
    private async Task OpenXmlAsync()
    {
        if (!CanOpenProject()) return;
        var xmlPath = PromptOpenXmlPath();
        if (xmlPath == null)
        {
            return;
        }

        await OpenXmlPathAsync(xmlPath);
    }

    /// <summary>Opens <paramref name="xmlPath"/> as an xTranslator XML project, from the open dialog or a dropped file.</summary>
    private async Task OpenXmlPathAsync(string xmlPath)
    {
        if (!CanOpenProject()) return;
        _isSwitchingProject = true;
        NotifyWorkspaceAvailability();
        // The current project's edits belong to its DB, which is disposed below. The workspace is already
        // disabled, so no row can be edited or left between this save and the switch. The new project reloads
        // the glossary and TM lists, so their unsaved grid edits are saved (or the open cancelled) first.
        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.All, "다른 파일을 열면")
            || !await TryCommitPendingDestEditsAsync())
        {
            _isSwitchingProject = false;
            NotifyWorkspaceAvailability();
            return;
        }

        using var loadCancellation = new CancellationTokenSource();
        _projectLoadCancellation = loadCancellation;
        try
        {
            await StopAllProjectOperationsAsync();
            loadCancellation.Token.ThrowIfCancellationRequested();
            StatusMessage = "XML을 불러오는 중...";
            await DisposeProjectDbAsync();
            ResetProjectState();
            var retained = await LoadProjectFromXmlAsync(xmlPath, loadCancellation.Token);

            IsProjectLoaded = true;
            StatusMessage = $"{Path.GetFileName(xmlPath)}에서 문자열 {TotalCount}개를 불러왔습니다."
                + (retained > 0
                    ? $" 이 파일에 없는 행의 번역 {retained}개는 지우지 않고 보관 중이며, 그 행이 든 XML을 다시 열면 복원됩니다."
                    : "")
                + DescribeUnavailableGlobalDb();
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested)
        {
            await DisposeProjectDbAsync();
            ResetProjectState();
            StatusMessage = "XML 불러오기를 중지했습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("XML 로드", ex);
            try
            {
                await DisposeProjectDbAsync();
            }
            finally
            {
                ResetProjectState();
            }
        }
        finally
        {
            _isSwitchingProject = false;
            _projectLoadCancellation = null;
            if (!_isClosing) _projectOperations.Resume();
            NotifyWorkspaceAvailability();
        }
    }

    private string? PromptOpenXmlPath()
        => _uiInteractionService.ShowOpenFileDialog(
            new OpenFileDialogRequest(
                Filter: "xTranslator XML (*.xml)|*.xml|All files (*.*)|*.*",
                Title: "xTranslator XML 열기"
            )
        );

    private void ResetProjectState()
    {
        ClearPendingRowUpdates();
        IsProjectLoaded = false;
        _projectState.Clear();
        OnPropertyChanged(nameof(CurrentXmlFileName));
        NotifyWorkspaceAvailability();
        ProjectContextPreview = "";
        PreviousTranslationSummary = PreviousTranslationUnavailable;
        HasPreviousTranslation = false;
        SelectedEntry = null;
        TotalCount = 0;
        DoneCount = 0;
        PendingCount = 0;
        ClearProjectLists();
        ClearProjectResults();
    }

    /// <summary>
    /// The glossary and TM grids hold rows of the DBs they were read from, and an open refills them only after
    /// the new project is in place. An ESP open cancelled at that point, or whose reload failed, left the previous
    /// project's rows over the new DB: saving one wrote its Id (they restart at 1) into the new project's glossary,
    /// and the quality check used the old terms. The lists now go with the project and come back with the reloads.
    /// </summary>
    private void ClearProjectLists()
    {
        SelectedGlossaryEntry = null;
        Glossary.Clear();
        RebuildGlossaryCategoryFilters();
        GlossaryView.Refresh();

        SelectedGlobalGlossaryEntry = null;
        GlobalGlossary.Clear();
        RebuildGlobalGlossaryCategoryFilters();
        GlobalGlossaryView.Refresh();

        SelectedFranchiseTranslationMemoryEntry = null;
        FranchiseTranslationMemory.Clear();
        FranchiseTranslationMemoryView.Refresh();

        RebuildGlossaryLookupResults();
    }

    private async Task DisposeProjectDbAsync()
    {
        await _projectState.DisposeDbAsync();
    }

    /// <returns>The number of translations kept from rows this XML does not contain.</returns>
    private async Task<int> LoadProjectFromXmlAsync(string xmlPath, CancellationToken cancellationToken = default)
    {
        var result = await _projectWorkspaceService.LoadFromXmlAsync(
            new ProjectWorkspaceService.LoadFromXmlRequest(
                XmlPath: xmlPath,
                SelectedFranchise: SelectedFranchise,
                SelectedModel: SelectedModel,
                CustomPromptText: CustomPromptText,
                UseCustomPrompt: UseCustomPrompt
            ),
            cancellationToken
        );

        _projectState.SetWorkspace(result.Db, result.XmlInfo, result.InputXmlPath);
        OnPropertyChanged(nameof(CurrentXmlFileName));

        SelectedFranchise = result.Franchise;
        SourceLang = result.SourceLang;
        TargetLang = result.TargetLang;

        cancellationToken.ThrowIfCancellationRequested();
        await _bundledFranchiseTmSeedService.EnsureBundledSeedAsync(SelectedFranchise, cancellationToken);
        await TryAutoImportFranchiseTranslationMemoryAsync();

        cancellationToken.ThrowIfCancellationRequested();
        await ReloadGlossaryAsync();
        await ReloadGlobalGlossaryAsync();
        await ReloadFranchiseTranslationMemoryAsync();
        await ReloadProjectContextAsync();
        await LoadEntriesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result.RetainedTranslationCount;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportXmlAsync()
    {
        var db = _projectState.Db;
        var xmlInfo = _projectState.XmlInfo;
        if (db == null || xmlInfo == null)
        {
            return;
        }

        // The exporter reads the DB; an edit still only in the editor would be missing from the XML.
        if (!await TryCommitPendingDestEditsAsync())
        {
            return;
        }

        if (!await ConfirmExportWithUnfinishedRowsAsync(db, xml: true))
        {
            return;
        }

        var exportPath = _uiInteractionService.ShowSaveFileDialog(
            new SaveFileDialogRequest(
                Filter: "xTranslator XML (*.xml)|*.xml|All files (*.*)|*.*",
                Title: "번역한 XML 내보내기",
                FileName: Path.GetFileNameWithoutExtension(xmlInfo.AddonName) + ".translated.xml"
            )
        );
        if (string.IsNullOrWhiteSpace(exportPath))
        {
            return;
        }

        try
        {
            StatusMessage = "XML을 내보내는 중...";
            await _projectWorkspaceService.ExportXmlAsync(db, xmlInfo, exportPath, CancellationToken.None);
            StatusMessage = $"내보냈습니다: {exportPath}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("XML 내보내기", ex);
        }
    }

    /// <summary>
    /// Saving with rows still in error or pending used to finish silently, so a half-translated file went out
    /// looking complete. A plugin keeps the source text of an unfinished row; an XML keeps its translation field as it
    /// is, empty or as the opened XML had it (the question used to say "source" for both). A hand edit left empty is
    /// saved empty, and a line agent mode held keeps its English: both went out without a word. The agent's export
    /// counts the same rows (ExportUnfinishedRows).
    /// </summary>
    private async Task<bool> ConfirmExportWithUnfinishedRowsAsync(ProjectDb db, bool xml)
    {
        ExportUnfinishedRows counts;
        try
        {
            counts = await ExportUnfinishedRows.CountAsync(db,
                Entries.Select(entry => (entry.Id, entry.Status, entry.SourceText, entry.DestText)).ToList(), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // It reads the held lines' notes from the DB: a failure there ended the command with the app's crash dialog.
            SetUserFacingError("저장 확인", ex);
            return false;
        }

        if (!counts.Any)
        {
            return true;
        }

        var (errors, pending, emptied, held) = counts;

        var answer = _uiInteractionService.ShowMessage(
            (errors + pending > 0 ? $"번역이 끝나지 않은 행이 있습니다: 오류 {errors}개, 대기 {pending}개.{Environment.NewLine}" : "")
            + (emptied > 0 ? $"번역문을 비워 둔 직접 수정 행이 {emptied}개 있습니다. 이 행들은 빈 글로 저장됩니다.{Environment.NewLine}" : "")
            + (held > 0 ? $"에이전트 모드에서 보류한(번역하지 않기로 한) 건너뜀 행이 {held}개 있습니다. 이 행들은 영어 그대로 저장됩니다.{Environment.NewLine}" : "")
            + Environment.NewLine
            + (errors + pending == 0 ? "그래도 저장할까요?"
                : xml
                ? "XML에는 끝나지 않은 행의 번역란이 지금 그대로 저장됩니다(비어 있으면 빈 채로). 그래도 저장할까요?"
                : "플러그인에는 끝나지 않은 행의 원문이 그대로 남습니다. 그래도 저장할까요?"),
            "저장 확인",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning,
            UiMessageBoxResult.No
        );
        if (answer == UiMessageBoxResult.Yes)
        {
            return true;
        }

        var filters = new[] { errors + pending > 0 ? "'오류'·'대기'" : null, emptied > 0 ? "'직접 수정'" : null, held > 0 ? "'건너뜀'" : null };
        StatusMessage = $"저장하지 않았습니다. 상태 필터에서 {string.Join("·", filters.OfType<string>())} 행을 확인하세요.";
        return false;
    }

    private bool CanExport() => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive && _projectState.XmlInfo != null;

    [RelayCommand(CanExecute = nameof(CanSaveSelectedDest))]
    private async Task SaveSelectedDestAsync()
    {
        if (SelectedEntry == null)
        {
            return;
        }

        // Nothing typed: saving would only turn a pending row into a manual edit of its empty or English text, which
        // later runs skip and the export writes as it is (and turn a model translation into a manual one).
        if (!SelectedEntry.HasUnsavedDestEdit)
        {
            StatusMessage = "바뀐 내용이 없어 저장하지 않았습니다.";
            return;
        }

        try
        {
            StatusMessage = await CommitDestEditAsync(SelectedEntry, SelectedEntry.DestText) switch
            {
                DestEditOutcome.Saved => "번역문 수정을 저장했습니다.",
                DestEditOutcome.BackToPending => "번역문을 비우고 대기로 돌렸습니다. '번역 시작'을 누르면 번역합니다.",
                _ => "저장하지 않았습니다. 편집기의 번역문은 그대로입니다.",
            };
        }
        catch (Exception ex)
        {
            SetUserFacingError("번역문 저장", ex);
        }
    }

    private bool CanSaveSelectedDest() => IsProjectLoaded && !IsTranslating && SelectedEntry != null;

    public async Task<DestEditOutcome> CommitDestEditAsync(StringEntryViewModel entry, string newDest)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return DestEditOutcome.NotSaved;
        }

        return await CommitDestEditAsync(db, entry, newDest);
    }

    /// <summary>What saving the editor's text did with the row.</summary>
    public enum DestEditOutcome
    {
        /// <summary>The user kept the text in the editor (an emptied translation, 취소).</summary>
        NotSaved,

        /// <summary>Saved as 직접 수정.</summary>
        Saved,

        /// <summary>The emptied row went back to 대기, to be translated by the next run.</summary>
        BackToPending,
    }

    private async Task<DestEditOutcome> CommitDestEditAsync(ProjectDb db, StringEntryViewModel entry, string newDest)
    {
        var savedDest = newDest ?? "";
        // An emptied translation was saved as a hand edit of nothing: every later run skips it and the export writes it
        // empty, as an XML's intentionally empty translation. Emptying is as often meant as "translate this again".
        if (IsEmptiedTranslation(entry, savedDest))
        {
            var answer = _uiInteractionService.ShowMessage(
                $"#{entry.OrderIndex} 행의 번역문을 비웠습니다.{Environment.NewLine}{Environment.NewLine}"
                + $"예: 빈 채로 저장합니다(직접 수정). ESP·XML에도 빈 글로 저장됩니다.{Environment.NewLine}"
                + $"아니요: 대기로 돌립니다. 다음 '번역 시작' 때 번역합니다.{Environment.NewLine}"
                + "취소: 지금은 저장하지 않습니다. 비운 번역문은 저장 전 상태로 남아, 내보내기·닫기 전에 다시 묻습니다.",
                "빈 번역 저장",
                UiMessageBoxButton.YesNoCancel,
                UiMessageBoxImage.Question,
                UiMessageBoxResult.Cancel
            );
            if (answer == UiMessageBoxResult.Cancel)
            {
                return DestEditOutcome.NotSaved;
            }

            if (answer == UiMessageBoxResult.No)
            {
                return await ReturnEmptiedRowToPendingAsync(db, entry) ? DestEditOutcome.BackToPending : DestEditOutcome.NotSaved;
            }
        }

        var shownWhenSaving = entry.DestText;
        await db.UpdateStringTranslationAsync(entry.Id, savedDest, StringEntryStatus.Edited, null, CancellationToken.None);
        // A row saved because the user left it can be reselected and typed into before this write finishes;
        // that newer text stays as the next unsaved edit instead of being replaced by the older saved text.
        if (string.Equals(entry.DestText, shownWhenSaving, StringComparison.Ordinal))
        {
            entry.DestText = savedDest;
        }
        entry.MarkDestTextSaved(savedDest);
        entry.Status = StringEntryStatus.Edited;
        entry.IsTranslationMemoryApplied = false;
        if (ReferenceEquals(db, _projectState.Db))
        {
            RecountProgress();
            try
            {
                await RecheckLqaRowAsync(entry);
            }
            catch (Exception ex)
            {
                AppLog.Write($"WARN 저장한 행을 다시 검사하지 못했습니다: {ex.Message}");
            }
        }

        return DestEditOutcome.Saved;
    }

    /// <summary>A translation emptied of a row that has something to translate: saving it asks first.</summary>
    private static bool IsEmptiedTranslation(StringEntryViewModel entry, string? newDest)
        => string.IsNullOrWhiteSpace(newDest) && !string.IsNullOrWhiteSpace(entry.SourceText);

    /// <summary>
    /// An emptied row put back to 대기 as 다시 번역 does: its marks go, and a hand edit's old text is noted so the project
    /// TM does not fill it back. False for a row a run is translating now (it is left as it is).
    /// </summary>
    private async Task<bool> ReturnEmptiedRowToPendingAsync(ProjectDb db, StringEntryViewModel entry)
    {
        var shownWhenSaving = entry.DestText;
        if (entry.Status == StringEntryStatus.Pending)
        {
            // An XML's untranslated row can come with text in its translation field.
            await db.UpdateStringTranslationAsync(entry.Id, "", StringEntryStatus.Pending, null, CancellationToken.None);
        }
        else if (await db.ResetForRetranslationAsync(new[] { entry.Id }, includeEdited: true, CancellationToken.None) == 0)
        {
            return false;
        }

        if (string.Equals(entry.DestText, shownWhenSaving, StringComparison.Ordinal))
        {
            entry.DestText = "";
        }
        entry.MarkDestTextSaved("");
        entry.Status = StringEntryStatus.Pending;
        entry.ErrorMessage = null;
        entry.IsTranslationMemoryApplied = false;
        entry.IsSourceFix = false;
        entry.SourceFixNeedsCheck = false;
        if (ReferenceEquals(db, _projectState.Db))
        {
            foreach (var key in _lqaReviewedMarks.Keys.Where(key => key.StringId == entry.Id).ToList())
            {
                _lqaReviewedMarks.Remove(key);
            }

            RecountProgress();
            try
            {
                await RecheckLqaRowAsync(entry);
            }
            catch (Exception ex)
            {
                AppLog.Write($"WARN 대기로 돌린 행을 다시 검사하지 못했습니다: {ex.Message}");
            }
        }

        return true;
    }

    /// <summary>
    /// 완료 counts translated and hand-edited rows; 대기 counts the rows the next "번역 시작" translates: pending and
    /// failed rows, and rows a run is still working on. The start of a run counted failed rows as waiting, while
    /// opening a project, saving an edit and retranslating counted only pending ones, so the same project showed
    /// a different 대기 depending on what happened last.
    /// </summary>
    private void RecountProgress()
    {
        // An XML's rows that already had a translation come in as Skipped; they are finished too (the quality check's
        // rule), or a fully translated XML showed 0%.
        DoneCount = Entries.Count(row => LqaStatus(row) is StringEntryStatus.Done or StringEntryStatus.Edited);
        PendingCount = Entries.Count(row => row.Status.IsUnfinished());
    }

    private async Task LoadEntriesAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var total = (int)Math.Min(int.MaxValue, await db.GetStringCountAsync(CancellationToken.None));
        TotalCount = total;

        var tmHitIds = await GetKnownTranslationIdsAsync(db, CancellationToken.None);
        var sourceFixIds = await db.GetSourceFixIdsForCheckAsync(CancellationToken.None);
        var sourceFixRecheck = await db.GetStringNotesByKindAsync(TranslationConstants.SourceFixRecheckNoteKind, CancellationToken.None);

        const int pageSize = 500;
        var loaded = new List<StringEntryViewModel>(capacity: total);

        for (var offset = 0; offset < total; offset += pageSize)
        {
            var rows = await db.GetStringsAsync(pageSize, offset, CancellationToken.None);
            foreach (var row in rows)
            {
                var vm = new StringEntryViewModel(row.Id, row.OrderIndex)
                {
                    Edid = row.Edid,
                    Rec = row.Rec,
                    SourceText = row.SourceText,
                    DestText = row.DestText,
                    Status = row.Status,
                    ErrorMessage = row.ErrorMessage,
                    IsTranslationMemoryApplied = tmHitIds.Contains(row.Id),
                    IsSourceFix = sourceFixIds.Contains(row.Id),
                    SourceFixNeedsCheck = sourceFixRecheck.ContainsKey(row.Id),
                };

                loaded.Add(vm);
            }
        }

        _projectState.SetEntries(loaded);
        RecountProgress();
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
