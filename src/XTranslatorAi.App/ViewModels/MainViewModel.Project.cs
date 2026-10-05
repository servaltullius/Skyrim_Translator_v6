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

        if (!ConfirmExportWithUnfinishedRows())
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
    /// looking complete. Unfinished rows keep their source text in the output.
    /// </summary>
    private bool ConfirmExportWithUnfinishedRows()
    {
        var errors = 0;
        var pending = 0;
        foreach (var entry in Entries)
        {
            if (entry.Status == StringEntryStatus.Error) errors++;
            else if (entry.Status is StringEntryStatus.Pending or StringEntryStatus.InProgress) pending++;
        }

        if (errors + pending == 0)
        {
            return true;
        }

        var answer = _uiInteractionService.ShowMessage(
            $"번역이 끝나지 않은 행이 있습니다: 오류 {errors}개, 대기 {pending}개.{Environment.NewLine}{Environment.NewLine}"
            + "이 행들은 원문 그대로 저장됩니다. 그래도 저장할까요?",
            "저장 확인",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning,
            UiMessageBoxResult.No
        );
        if (answer == UiMessageBoxResult.Yes)
        {
            return true;
        }

        StatusMessage = "저장하지 않았습니다. 상태 필터에서 '오류'·'대기' 행을 확인하세요.";
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
            await CommitDestEditAsync(SelectedEntry, SelectedEntry.DestText);
            StatusMessage = "번역문 수정을 저장했습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("번역문 저장", ex);
        }
    }

    private bool CanSaveSelectedDest() => IsProjectLoaded && !IsTranslating && SelectedEntry != null;

    public async Task CommitDestEditAsync(StringEntryViewModel entry, string newDest)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        await CommitDestEditAsync(db, entry, newDest);
    }

    private async Task CommitDestEditAsync(ProjectDb db, StringEntryViewModel entry, string newDest)
    {
        var savedDest = newDest ?? "";
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
    }

    /// <summary>
    /// 완료 counts translated and hand-edited rows; 대기 counts the rows the next "번역 시작" translates: pending and
    /// failed rows, and rows a run is still working on. The start of a run counted failed rows as waiting, while
    /// opening a project, saving an edit and retranslating counted only pending ones, so the same project showed
    /// a different 대기 depending on what happened last.
    /// </summary>
    private void RecountProgress()
    {
        DoneCount = Entries.Count(row => row.Status is StringEntryStatus.Done or StringEntryStatus.Edited);
        PendingCount = Entries.Count(row => row.Status is StringEntryStatus.Pending or StringEntryStatus.Error or StringEntryStatus.InProgress);
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

        var tmHitIds = new System.Collections.Generic.HashSet<long>(
            (await db.GetStringNotesByKindAsync(TranslationConstants.TmHitNoteKind, CancellationToken.None)).Keys
        );

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
                };

                loaded.Add(vm);
            }
        }

        _projectState.SetEntries(loaded);
        RecountProgress();
        SelectedEntry = Entries.Count > 0 ? Entries[0] : null;
    }
}
