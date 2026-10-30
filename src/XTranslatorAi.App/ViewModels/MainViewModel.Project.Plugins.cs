using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    public IReadOnlyList<string> PluginEncodingChoices { get; } = new[] { "utf-8", "windows-1252", "ks_c_5601-1987" };
    public IReadOnlyList<string> PluginLanguageChoices { get; } = new[] { "english", "korean", "japanese", "german", "french", "spanish", "italian", "russian", "chinese" };
    public IReadOnlyList<string> PluginGameChoices { get; } = new[] { "Skyrim Special Edition / Anniversary Edition" };
    public string PluginGameSelection { get; set; } = "Skyrim Special Edition / Anniversary Edition";
    [ObservableProperty] private string _pluginSourceLanguage = "english";
    [ObservableProperty] private string _pluginTargetLanguage = "korean";
    [ObservableProperty] private string _pluginSourceEncoding = "utf-8";
    [ObservableProperty] private string _pluginMetadataEncoding = "windows-1252";
    [ObservableProperty] private string _pluginTargetEncoding = "utf-8";
    [ObservableProperty] private string _pluginStringsDirectory = "";
    [ObservableProperty] private bool _isPluginIoBusy;
    private CancellationTokenSource? _pluginIoCancellation;

    partial void OnIsPluginIoBusyChanged(bool value) => NotifyWorkspaceAvailability();

    [RelayCommand]
    private void CancelPluginIo()
    {
        _pluginIoCancellation?.Cancel();
        _projectLoadCancellation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanOpenProject))]
    private async Task OpenPluginAsync()
    {
        if (!CanOpenProject()) return;
        var path = _uiInteractionService.ShowOpenFileDialog(new OpenFileDialogRequest(
            "Skyrim SE/AE 플러그인 (*.esp;*.esm;*.esl)|*.esp;*.esm;*.esl", "번역할 Skyrim SE/AE 플러그인 열기"));
        if (string.IsNullOrWhiteSpace(path)) return;
        await OpenPluginPathAsync(path);
    }

    /// <summary>Opens <paramref name="path"/> as a plugin project, from the open dialog or a dropped file.</summary>
    private async Task OpenPluginPathAsync(string path)
    {
        if (!CanOpenProject()) return;
        var options = new PluginReadOptions(PluginGame.SkyrimSpecialEdition, (PluginSourceLanguage ?? "").Trim(),
            PluginSourceEncoding, string.IsNullOrWhiteSpace(PluginStringsDirectory) ? null : PluginStringsDirectory.Trim(),
            MetadataEncoding: PluginMetadataEncoding);
        var request = new ProjectWorkspaceService.LoadFromPluginRequest(path, options,
            (PluginTargetLanguage ?? "").Trim(), PluginTargetEncoding, SelectedModel, CustomPromptText, UseCustomPrompt);

        _isSwitchingProject = true;
        IsPluginIoBusy = true;
        // The current project's edits belong to its DB, which this open replaces (see OpenXmlPathAsync).
        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.All, "다른 파일을 열면")
            || !await TryCommitPendingDestEditsAsync())
        {
            _isSwitchingProject = false;
            IsPluginIoBusy = false;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _projectLoadCancellation = cancellation;
        ProjectDb? incomingDb = null;
        var adopted = false;
        try
        {
            await StopAllProjectOperationsAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            StatusMessage = "플러그인을 읽고 번역 가능한 문자열을 확인하는 중...";
            var loaded = await _projectWorkspaceService.LoadFromPluginAsync(request, cancellation.Token);
            incomingDb = loaded.Db;
            // The rows' notes the import kept: without them the grid lost its TM color and the quality check, which
            // reads the "원문 오류 수정" mark from the rows, flagged the marked rows again.
            var tmHitIds = await GetKnownTranslationIdsAsync(loaded.Db, CancellationToken.None);
            var sourceFixIds = await loaded.Db.GetSourceFixIdsForCheckAsync(CancellationToken.None);
            var sourceFixRecheck = await loaded.Db.GetStringNotesByKindAsync(TranslationConstants.SourceFixRecheckNoteKind, CancellationToken.None);
            var entries = loaded.Entries.Select(row => new StringEntryViewModel(row.Id, row.OrderIndex)
            {
                Edid = row.Edid, Rec = row.Rec, SourceText = row.SourceText, DestText = row.DestText,
                Status = row.Status, ErrorMessage = row.ErrorMessage,
                PluginLocation = loaded.Document.Fields[row.OrderIndex],
                IsTranslationMemoryApplied = tmHitIds.Contains(row.Id),
                IsSourceFix = sourceFixIds.Contains(row.Id),
                SourceFixNeedsCheck = sourceFixRecheck.ContainsKey(row.Id),
            }).ToList();
            // The service's import commit is the point of no cancellation. Adopt its ready snapshot
            // before awaiting auxiliary work, so a late cancellation never leaves old rows over a new DB.
            var previousDb = _projectState.Db;
            ResetProjectState();
            _projectState.SetPluginWorkspace(loaded.Db, loaded.Document, loaded.TargetEncoding, request.Options.SourceEncoding);
            incomingDb = null;
            adopted = true;
            _projectState.SetEntries(entries);
            TotalCount = entries.Count;
            RecountProgress();
            SelectedEntry = entries.FirstOrDefault();
            ProjectContextPreview = loaded.ProjectContext;
            OnPropertyChanged(nameof(CurrentXmlFileName));
            SelectedFranchise = BethesdaFranchise.ElderScrolls;
            SourceLang = loaded.SourceLanguage;
            TargetLang = loaded.TargetLanguage;
            IsProjectLoaded = true;
            if (previousDb != null) await previousDb.DisposeAsync();
            // A late cancel may skip auxiliary refresh only after the committed DB and rows are visible.
            cancellation.Token.ThrowIfCancellationRequested();
            await _bundledFranchiseTmSeedService.EnsureBundledSeedAsync(SelectedFranchise, CancellationToken.None);
            await TryAutoImportFranchiseTranslationMemoryAsync();
            await ReloadGlossaryAsync();
            await ReloadGlobalGlossaryAsync();
            await ReloadFranchiseTranslationMemoryAsync();
            await RefreshPreviousTranslationsAsync(CancellationToken.None);
            // The agent's open says the same (PluginOpenNotes).
            StatusMessage = $"플러그인 읽기 완료: {Path.GetFileName(path)} · {TotalCount}개 문자열"
                + string.Concat(PluginOpenNotes.Describe(loaded).Select(note => " · " + note.Text))
                + DescribeUnavailableGlobalDb();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = adopted ? "플러그인은 열렸습니다. 보조 데이터 새로고침을 중지했습니다." : "플러그인 읽기를 중지했습니다.";
        }
        catch (Exception ex)
        {
            if (adopted) SetUserFacingError("플러그인은 열렸지만 보조 데이터 새로고침", ex);
            else SetPluginUserFacingError("플러그인 읽기", ex);
        }
        finally
        {
            if (incomingDb != null) await incomingDb.DisposeAsync();
            _isSwitchingProject = false;
            _projectLoadCancellation = null;
            IsPluginIoBusy = false;
            if (!_isClosing) _projectOperations.Resume();
            NotifyWorkspaceAvailability();
        }
    }

    /// <summary>
    /// A translation the writer refuses is named by its rows (PluginExportErrors, as the agent's export names them): a
    /// lost tag was told as a model's answer breaking a token rule ("자동 복구를 켜고 다시 시도하세요"), naming no row,
    /// and an empty or unencodable translation by its record alone. The shared-string refusal names its rows itself.
    /// </summary>
    private async Task<bool> TryDescribeRefusedRowsAsync(ProjectDb db, PluginDocument document, Exception ex)
    {
        var classified = ex is InvalidDataException ? PluginUserFacingErrorClassifier.Classify(ex) : null;
        if (ex is not InvalidOperationException && (classified is not { } found || found.Message.Contains("관련 행", StringComparison.Ordinal))) return false;
        IReadOnlyList<long> ids;
        try
        {
            ids = await PluginExportErrors.RowIdsAsync(db, document, ex.Message, CancellationToken.None);
        }
        catch (Exception lookup) when (lookup is not OperationCanceledException)
        {
            AppLog.Write($"WARN 저장을 막은 행을 찾지 못했습니다: {lookup.Message}");
            return false;
        }

        var rows = ids.Select(id => _projectState.TryGetById(id, out var entry) ? entry.OrderIndex : (int?)null).OfType<int>().ToList();
        if (rows.Count == 0) return false;
        var named = string.Join(", ", rows.Take(12).Select(row => "#" + row)) + (rows.Count > 12 ? " …" : "");
        var code = classified?.Code ?? "E330";
        AppLog.WriteError(code, "플러그인 저장", ex);
        StatusMessage = classified is { } error
            ? $"플러그인 저장({code}): {error.Message} 관련 행: {named} (Search에 row:{rows[0]})"
            : $"플러그인 저장({code}): {named} 행의 번역이 원문의 태그·토큰과 맞지 않아 저장하지 않았습니다. "
              + $"검색 칸에 row:{rows[0]}를 넣고 상태를 (전체)로 두어 행을 찾아 고친 뒤 다시 저장하세요.";
        return true;
    }

    internal static string FindFreeOutputDirectory(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{path} ({n})";
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private bool CanExportPlugin() => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive
        && !_projectOperations.IsRunning && _projectState.PluginDocument != null
        && !_projectState.PluginDocument.Info.Diagnostics.Any(item => item.BlocksExport);

    [RelayCommand(CanExecute = nameof(CanExportPlugin))]
    private async Task ExportPluginAsync()
    {
        if (!CanExportPlugin()) return;
        var db = _projectState.Db;
        var document = _projectState.PluginDocument;
        if (db == null || document == null) return;
        // The plugin writer reads the DB; an edit still only in the editor would be missing from the ESP.
        if (!await TryCommitPendingDestEditsAsync()) return;
        if (!await ConfirmExportWithUnfinishedRowsAsync(db, xml: false)) return;
        var outputDirectory = _uiInteractionService.ShowSaveFileDialog(new SaveFileDialogRequest(
            "새 출력 폴더 이름|*.*", "새 출력 폴더 이름을 지정하세요 (폴더 안에 원본 파일명으로 저장)",
            Path.GetFileNameWithoutExtension(document.Info.InputPath) + ".translated"));
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;
        // An existing folder used to end the save with a message, after the dialog had closed, so the whole save had
        // to be started again. The output still never goes into an existing folder: a free name is chosen instead.
        outputDirectory = FindFreeOutputDirectory(outputDirectory);
        // Translation language and the game's localized filename slot are different:
        // keep the source slot (e.g. _english) unless a future explicit export option requests another.
        var options = new PluginExportOptions(outputDirectory, _projectState.PluginTargetEncoding ?? "utf-8");
        await RunProjectOperationAsync("플러그인 저장", async token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
            _pluginIoCancellation = linked;
            IsPluginIoBusy = true;
            try
            {
                StatusMessage = "플러그인을 새 폴더에 저장하고 검증하는 중...";
                var result = await _projectWorkspaceService.ExportPluginAsync(db, document, options, linked.Token);
                StatusMessage = $"플러그인 저장 완료: {result.PluginPath} · 변경 문자열 {result.ChangedFields}개";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!await TryDescribeRefusedRowsAsync(db, document, ex)) SetPluginUserFacingError("플러그인 저장", ex);
            }
            finally
            {
                _pluginIoCancellation = null;
                IsPluginIoBusy = false;
            }
        });
    }
}
