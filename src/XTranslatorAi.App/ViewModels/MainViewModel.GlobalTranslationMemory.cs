using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    // The game DB the listed TM rows were read from; their Ids mean nothing in another game's DB
    // (see _globalGlossaryRowsFranchise).
    private BethesdaFranchise _franchiseTmRowsFranchise = BethesdaFranchise.ElderScrolls;

    private bool AreFranchiseTmRowsFromSelectedFranchise()
    {
        if (_franchiseTmRowsFranchise == _globalProjectDbService.SelectedFranchise)
        {
            return true;
        }

        StatusMessage = "시리즈 TM 목록이 이전 게임 시리즈의 것이라 저장·삭제하지 않았습니다. 새로고침한 뒤 다시 시도하세요.";
        return false;
    }

    private async Task TryAutoImportFranchiseTranslationMemoryAsync()
    {
        if (IsTranslating)
        {
            return;
        }

        try
        {
            var importDir = ProjectPaths.GetGlobalTranslationMemoryImportDir(SelectedFranchise);
            if (!Directory.Exists(importDir))
            {
                return;
            }

            var files = Directory
                .GetFiles(importDir, "*.tsv", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
            {
                return;
            }

            var importedDir = Path.Combine(importDir, "imported");
            Directory.CreateDirectory(importedDir);

            var totalApplied = 0;
            string? unreadable = null;
            foreach (var path in files)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                StatusMessage = $"시리즈 TM 자동 가져오기: {Path.GetFileName(path)}";
                int applied;
                try
                {
                    applied = await _globalTranslationMemoryService.ImportFromTsvAsync(
                        SourceLang.Trim(),
                        TargetLang.Trim(),
                        path,
                        CancellationToken.None
                    );
                }
                catch (ImportFileEncodingException ex)
                {
                    // Left in tm-import for the user to re-save; the other files are still imported.
                    unreadable ??= ex.Message;
                    continue;
                }

                totalApplied += Math.Max(0, applied);

                var stem = Path.GetFileNameWithoutExtension(path);
                var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                var destName = $"{stem}.imported.{timestamp}.tsv";
                var destPath = Path.Combine(importedDir, destName);
                File.Move(path, destPath, overwrite: false);
            }

            if (unreadable != null)
            {
                StatusMessage = $"시리즈 TM 자동 가져오기: {unreadable}";
            }
            else if (totalApplied > 0)
            {
                StatusMessage = $"시리즈 TM 자동 가져오기 완료: {totalApplied}개 항목";
            }
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 자동 가져오기", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadFranchiseTranslationMemory))]
    private async Task ReloadFranchiseTranslationMemoryAsync()
    {
        var franchise = _globalProjectDbService.SelectedFranchise;
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            FranchiseTranslationMemory.Clear();
            FranchiseTranslationMemoryView.Refresh();
            StatusMessage = "시리즈 TM DB를 초기화하지 못했습니다.";
            return;
        }

        try
        {
            StatusMessage = "시리즈 TM을 불러오는 중...";
            var rows = await _globalTranslationMemoryService.GetEntriesAsync(SourceLang.Trim(), TargetLang.Trim(), CancellationToken.None);
            // A later game-series switch started its own reload; the earlier game's rows must not replace it.
            if (franchise != _globalProjectDbService.SelectedFranchise)
            {
                return;
            }

            var list = rows
                .Select(
                    r =>
                    {
                        var vm = new TranslationMemoryEntryViewModel(r.Id);
                        vm.BeginUpdate();
                        vm.SourceText = r.SourceText;
                        vm.DestText = r.DestText;
                        vm.UpdatedAt = r.UpdatedAt;
                        vm.EndUpdate();
                        vm.MarkClean();
                        return vm;
                    }
                )
                .ToList();

            FranchiseTranslationMemory.ReplaceAll(list);
            _franchiseTmRowsFranchise = franchise;
            FranchiseTranslationMemoryView.Refresh();
            StatusMessage = $"시리즈 TM 불러오기: {list.Count}개 항목";
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 불러오기", ex);
        }
    }

    private bool CanReloadFranchiseTranslationMemory() => !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanAddFranchiseTranslationMemory))]
    private async Task AddFranchiseTranslationMemoryAsync()
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "시리즈 TM DB를 초기화하지 못했습니다.";
            return;
        }

        var src = (FranchiseTranslationMemorySourceText ?? "").Trim();
        var dst = (FranchiseTranslationMemoryDestText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
        {
            return;
        }

        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.FranchiseTranslationMemory, "시리즈 TM에 추가하면"))
        {
            return;
        }

        try
        {
            var applied = await _globalTranslationMemoryService.BulkUpsertAsync(
                SourceLang.Trim(),
                TargetLang.Trim(),
                new List<(string SourceText, string DestText)> { (src, dst) },
                CancellationToken.None
            );

            FranchiseTranslationMemorySourceText = "";
            FranchiseTranslationMemoryDestText = "";

            await ReloadFranchiseTranslationMemoryAsync();
            StatusMessage = applied > 0 ? "시리즈 TM 추가·갱신을 마쳤습니다." : "시리즈 TM에 추가·갱신할 항목이 없습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 추가", ex);
        }
    }

    private bool CanAddFranchiseTranslationMemory() => !IsTranslating
        && !string.IsNullOrWhiteSpace(FranchiseTranslationMemorySourceText)
        && !string.IsNullOrWhiteSpace(FranchiseTranslationMemoryDestText);

    [RelayCommand(CanExecute = nameof(CanSaveFranchiseTranslationMemoryChanges))]
    private async Task SaveFranchiseTranslationMemoryChangesAsync()
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "시리즈 TM DB를 초기화하지 못했습니다.";
            return;
        }

        var dirty = FranchiseTranslationMemory.Where(e => e.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = "저장할 변경사항이 없습니다.";
            return;
        }

        if (!AreFranchiseTmRowsFromSelectedFranchise())
        {
            return;
        }

        try
        {
            var applied = await SaveFranchiseTranslationMemoryRowsAsync(dirty);
            await ReloadFranchiseTranslationMemoryAsync();
            StatusMessage = $"시리즈 TM 저장 완료: {applied}개 항목";
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 저장", ex);
        }
    }

    private bool CanSaveFranchiseTranslationMemoryChanges() => !IsTranslating;

    private async Task<int> SaveFranchiseTranslationMemoryRowsAsync(IReadOnlyList<TranslationMemoryEntryViewModel> dirty)
    {
        var rows = dirty
            .Select(e => (e.Id, DestText: (e.DestText ?? "").Trim()))
            .ToList();

        var applied = await _globalTranslationMemoryService.BulkUpdateAsync(SourceLang.Trim(), TargetLang.Trim(), rows, CancellationToken.None);
        foreach (var e in dirty)
        {
            e.MarkClean();
        }

        return applied;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteFranchiseTranslationMemoryEntry))]
    private async Task DeleteFranchiseTranslationMemoryEntryAsync()
    {
        if (SelectedFranchiseTranslationMemoryEntry == null
            || !AreFranchiseTmRowsFromSelectedFranchise()
            || await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            return;
        }

        var confirm = _uiInteractionService.ShowMessage(
            $"선택한 시리즈 TM 항목을 삭제할까요?\n\n- {SelectedFranchiseTranslationMemoryEntry.SourceText} => {SelectedFranchiseTranslationMemoryEntry.DestText}",
            "시리즈 TM 삭제",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning
        );
        if (confirm != UiMessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var ids = new List<long> { SelectedFranchiseTranslationMemoryEntry.Id };
            var removed = await _globalTranslationMemoryService.DeleteAsync(SourceLang.Trim(), TargetLang.Trim(), ids, CancellationToken.None);
            if (removed > 0)
            {
                FranchiseTranslationMemory.Remove(SelectedFranchiseTranslationMemoryEntry);
                SelectedFranchiseTranslationMemoryEntry = null;
                FranchiseTranslationMemoryView.Refresh();
            }

            StatusMessage = removed > 0 ? "시리즈 TM 항목을 삭제했습니다." : "삭제할 항목이 없습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 삭제", ex);
        }
    }

    private bool CanDeleteFranchiseTranslationMemoryEntry() => !IsTranslating && SelectedFranchiseTranslationMemoryEntry != null;

    [RelayCommand(CanExecute = nameof(CanExportFranchiseTranslationMemory))]
    private async Task ExportFranchiseTranslationMemoryAsync()
    {
        var path = ResolveGlossaryExportPath(title: "Export franchise TM", defaultFileName: "franchise-tm.tsv");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var rows = FranchiseTranslationMemory
                .Select(e => (SourceText: e.SourceText ?? "", DestText: e.DestText ?? ""));
            await File.WriteAllTextAsync(path, TranslationMemoryFileService.BuildTsv(rows), CancellationToken.None);
            StatusMessage = $"시리즈 TM을 내보냈습니다: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 내보내기", ex);
        }
    }

    private bool CanExportFranchiseTranslationMemory() => !IsTranslating;

    partial void OnFranchiseTranslationMemoryFilterTextChanged(string value) => FranchiseTranslationMemoryView.Refresh();

    private bool FranchiseTranslationMemoryFilter(object obj)
    {
        if (obj is not TranslationMemoryEntryViewModel entry)
        {
            return true;
        }

        var q = (FranchiseTranslationMemoryFilterText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return (entry.SourceText ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (entry.DestText ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanImportFranchiseTranslationMemoryFromTab))]
    private async Task ImportFranchiseTranslationMemoryFromTabAsync()
    {
        var filePath = _uiInteractionService.ShowOpenFileDialog(
            new OpenFileDialogRequest(
                Filter: "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
                Title: "Import franchise TM (TSV: Source<TAB>Target)"
            )
        );
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.FranchiseTranslationMemory, "파일에서 가져오면"))
        {
            return;
        }

        await ImportFranchiseTranslationMemoryFromTsvPathAsync(filePath, reloadAfterImport: true);
    }

    private bool CanImportFranchiseTranslationMemoryFromTab() => !IsTranslating;

    private async Task ImportFranchiseTranslationMemoryFromTsvPathAsync(string tsvPath, bool reloadAfterImport)
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "시리즈 TM DB를 초기화하지 못했습니다.";
            return;
        }

        try
        {
            StatusMessage = "시리즈 TM을 가져오는 중...";
            var applied = await _globalTranslationMemoryService.ImportFromTsvAsync(SourceLang.Trim(), TargetLang.Trim(), tsvPath, CancellationToken.None);
            if (applied <= 0)
            {
                StatusMessage = "가져올 항목이 없습니다.";
                return;
            }

            if (reloadAfterImport)
            {
                await ReloadFranchiseTranslationMemoryAsync();
            }

            StatusMessage = $"시리즈 TM 가져오기 완료: {applied}개 항목";
        }
        catch (ImportFileEncodingException ex)
        {
            // The generic classifier would hide this behind "예상치 못한 오류(E999)".
            StatusMessage = $"시리즈 TM 가져오기: {ex.Message}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("시리즈 TM 가져오기", ex);
        }
    }
}
