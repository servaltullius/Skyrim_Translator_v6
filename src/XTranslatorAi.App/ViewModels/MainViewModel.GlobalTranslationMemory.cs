using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private async Task TryAutoImportGlobalTranslationMemoryAsync()
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
            foreach (var path in files)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                StatusMessage = $"Global TM 자동 가져오기: {Path.GetFileName(path)}";
                var applied = await _globalTranslationMemoryService.ImportFromTsvAsync(
                    SourceLang.Trim(),
                    TargetLang.Trim(),
                    path,
                    CancellationToken.None
                );
                totalApplied += Math.Max(0, applied);

                var stem = Path.GetFileNameWithoutExtension(path);
                var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                var destName = $"{stem}.imported.{timestamp}.tsv";
                var destPath = Path.Combine(importedDir, destName);
                File.Move(path, destPath, overwrite: false);
            }

            if (totalApplied > 0)
            {
                StatusMessage = $"Global TM 자동 가져오기 완료: {totalApplied}개 항목";
            }
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM 자동 가져오기", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanReloadGlobalTranslationMemory))]
    private async Task ReloadGlobalTranslationMemoryAsync()
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            GlobalTranslationMemory.Clear();
            GlobalTranslationMemoryView.Refresh();
            StatusMessage = "Global DB 초기화에 실패했습니다.";
            return;
        }

        try
        {
            StatusMessage = "Global TM 불러오는 중...";
            var rows = await _globalTranslationMemoryService.GetEntriesAsync(SourceLang.Trim(), TargetLang.Trim(), CancellationToken.None);
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

            GlobalTranslationMemory.ReplaceAll(list);
            GlobalTranslationMemoryView.Refresh();
            StatusMessage = $"Global TM 로드: {list.Count}개 항목";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM 로드", ex);
        }
    }

    private bool CanReloadGlobalTranslationMemory() => !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanAddGlobalTranslationMemory))]
    private async Task AddGlobalTranslationMemoryAsync()
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "Global DB 초기화에 실패했습니다.";
            return;
        }

        var src = (GlobalTranslationMemorySourceText ?? "").Trim();
        var dst = (GlobalTranslationMemoryDestText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
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

            GlobalTranslationMemorySourceText = "";
            GlobalTranslationMemoryDestText = "";

            await ReloadGlobalTranslationMemoryAsync();
            StatusMessage = applied > 0 ? "Global TM 추가/갱신 완료." : "Global TM 추가/갱신할 항목이 없습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM 추가", ex);
        }
    }

    private bool CanAddGlobalTranslationMemory() => !IsTranslating
        && !string.IsNullOrWhiteSpace(GlobalTranslationMemorySourceText)
        && !string.IsNullOrWhiteSpace(GlobalTranslationMemoryDestText);

    [RelayCommand(CanExecute = nameof(CanSaveGlobalTranslationMemoryChanges))]
    private async Task SaveGlobalTranslationMemoryChangesAsync()
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "Global DB 초기화에 실패했습니다.";
            return;
        }

        var dirty = GlobalTranslationMemory.Where(e => e.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = "저장할 변경사항이 없습니다.";
            return;
        }

        try
        {
            var rows = dirty
                .Select(e => (e.Id, DestText: (e.DestText ?? "").Trim()))
                .ToList();

            var applied = await _globalTranslationMemoryService.BulkUpdateAsync(SourceLang.Trim(), TargetLang.Trim(), rows, CancellationToken.None);
            await ReloadGlobalTranslationMemoryAsync();
            StatusMessage = $"Global TM 저장 완료: {applied}개 항목";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM 저장", ex);
        }
    }

    private bool CanSaveGlobalTranslationMemoryChanges() => !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanDeleteGlobalTranslationMemoryEntry))]
    private async Task DeleteGlobalTranslationMemoryEntryAsync()
    {
        if (SelectedGlobalTranslationMemoryEntry == null
            || await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            return;
        }

        var confirm = _uiInteractionService.ShowMessage(
            $"선택한 Global TM 항목을 삭제할까요?\n\n- {SelectedGlobalTranslationMemoryEntry.SourceText} => {SelectedGlobalTranslationMemoryEntry.DestText}",
            "Global TM 삭제",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning
        );
        if (confirm != UiMessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var ids = new List<long> { SelectedGlobalTranslationMemoryEntry.Id };
            var removed = await _globalTranslationMemoryService.DeleteAsync(SourceLang.Trim(), TargetLang.Trim(), ids, CancellationToken.None);
            if (removed > 0)
            {
                GlobalTranslationMemory.Remove(SelectedGlobalTranslationMemoryEntry);
                SelectedGlobalTranslationMemoryEntry = null;
                GlobalTranslationMemoryView.Refresh();
            }

            StatusMessage = removed > 0 ? "Global TM 항목 삭제 완료." : "삭제할 항목이 없습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM 삭제", ex);
        }
    }

    private bool CanDeleteGlobalTranslationMemoryEntry() => !IsTranslating && SelectedGlobalTranslationMemoryEntry != null;

    [RelayCommand(CanExecute = nameof(CanExportGlobalTranslationMemory))]
    private async Task ExportGlobalTranslationMemoryAsync()
    {
        var path = ResolveGlossaryExportPath(title: "Export global TM", defaultFileName: "global-tm.tsv");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var rows = GlobalTranslationMemory
                .Select(e => (SourceText: e.SourceText ?? "", DestText: e.DestText ?? ""));
            await File.WriteAllTextAsync(path, TranslationMemoryFileService.BuildTsv(rows), CancellationToken.None);
            StatusMessage = $"Global TM exported: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM export", ex);
        }
    }

    private bool CanExportGlobalTranslationMemory() => !IsTranslating;

    partial void OnGlobalTranslationMemoryFilterTextChanged(string value) => GlobalTranslationMemoryView.Refresh();

    private bool GlobalTranslationMemoryFilter(object obj)
    {
        if (obj is not TranslationMemoryEntryViewModel entry)
        {
            return true;
        }

        var q = (GlobalTranslationMemoryFilterText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            return true;
        }

        return (entry.SourceText ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
            || (entry.DestText ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanImportGlobalTranslationMemoryFromTab))]
    private async Task ImportGlobalTranslationMemoryFromTabAsync()
    {
        var filePath = _uiInteractionService.ShowOpenFileDialog(
            new OpenFileDialogRequest(
                Filter: "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
                Title: "Import global TM (TSV: Source<TAB>Target)"
            )
        );
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await ImportGlobalTranslationMemoryFromTsvPathAsync(filePath, reloadAfterImport: true);
    }

    private bool CanImportGlobalTranslationMemoryFromTab() => !IsTranslating;

    private async Task ImportGlobalTranslationMemoryFromTsvPathAsync(string tsvPath, bool reloadAfterImport)
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "Global DB 초기화에 실패했습니다.";
            return;
        }

        try
        {
            StatusMessage = "Global TM 가져오는 중...";
            var applied = await _globalTranslationMemoryService.ImportFromTsvAsync(SourceLang.Trim(), TargetLang.Trim(), tsvPath, CancellationToken.None);
            if (applied <= 0)
            {
                StatusMessage = "가져올 항목이 없습니다.";
                return;
            }

            if (reloadAfterImport)
            {
                await ReloadGlobalTranslationMemoryAsync();
            }

            StatusMessage = $"Global TM 가져오기 완료: {applied}개 항목";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global TM import", ex);
        }
    }
}
