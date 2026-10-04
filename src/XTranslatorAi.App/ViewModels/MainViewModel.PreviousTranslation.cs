using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Plugins;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// Links an earlier translated release of the open plugin (for example the previous Korean patch of a mod)
/// so the model sees how each field was translated before and keeps the names a human translator chose.
/// </summary>
public partial class MainViewModel
{
    private const string PreviousTranslationUnavailable = "ESP로 연 프로젝트에서 쓸 수 있습니다.";

    [ObservableProperty] private string _previousTranslationSummary = PreviousTranslationUnavailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearPreviousTranslationCommand))]
    private bool _hasPreviousTranslation;

    private bool CanImportPreviousTranslation()
        => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive && _projectState.PluginDocument != null;

    private bool CanClearPreviousTranslation() => CanImportPreviousTranslation() && HasPreviousTranslation;

    [RelayCommand(CanExecute = nameof(CanImportPreviousTranslation))]
    private async Task ImportPreviousTranslationAsync()
    {
        var document = _projectState.PluginDocument;
        if (document == null)
        {
            return;
        }

        var path = _uiInteractionService.ShowOpenFileDialog(new OpenFileDialogRequest(
            "Skyrim SE/AE 플러그인 (*.esp;*.esm;*.esl)|*.esp;*.esm;*.esl",
            "이전에 번역된 같은 플러그인 선택 (예: 이전 버전 한글판)"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ImportPreviousTranslationFromPathAsync(path);
    }

    /// <summary>Links <paramref name="path"/> as the earlier translation, from the open dialog or a dropped file.</summary>
    private async Task ImportPreviousTranslationFromPathAsync(string path)
    {
        var document = _projectState.PluginDocument;
        if (document == null || !CanImportPreviousTranslation())
        {
            return;
        }

        await RunProjectOperationAsync("이전 번역 불러오기", async cancellationToken =>
        {
            var db = _projectState.Db;
            if (db == null)
            {
                return;
            }

            StatusMessage = "이전 번역 플러그인을 읽는 중...";
            // A translated release is written in the target encoding, with its Strings next to it.
            var options = document.Info.Options with
            {
                SourceEncoding = _projectState.PluginTargetEncoding ?? PluginTargetEncoding,
                StringsDirectory = null,
                ArchivePaths = null,
            };
            var previous = await Task.Run(() => PluginReader.ReadAsync(path, options, cancellationToken), cancellationToken);
            // The master lists let a release that added or dropped a master still pair its FormIDs.
            var result = PreviousTranslationMatcher.Match(document.Fields, document.Info.Masters,
                previous.Fields, previous.Info.Masters, LanguageHelper.IsKoreanLanguage(TargetLang));
            if (result.TextByFieldKey.Count == 0)
            {
                StatusMessage = $"이전 번역을 연결하지 못했습니다: {Path.GetFileName(path)}에서 같은 레코드의 번역문을 찾지 못했습니다. "
                    + "같은 모드의 이전 번역판인지 확인하세요.";
                return;
            }

            await db.ReplacePreviousTranslationsAsync(Path.GetFileName(path), result.TextByFieldKey, cancellationToken);
            if (!ReferenceEquals(db, _projectState.Db))
            {
                return;
            }

            await RefreshPreviousTranslationsAsync(cancellationToken);
            StatusMessage = $"이전 번역 {result.TextByFieldKey.Count}행을 연결했습니다({Path.GetFileName(path)}). "
                + $"제외: 이전 판에 없음 {result.NotInPreviousRelease}, 레코드 구성이 바뀜 {result.ChangedRecord}, "
                + $"원문 그대로 {result.SameAsSource}, 번역 안 됨 {result.NotTranslated}. "
                + "다음 번역부터 참고합니다. 이미 번역된 행에 반영하려면 '다시 번역'을 쓰세요.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanClearPreviousTranslation))]
    private async Task ClearPreviousTranslationAsync()
    {
        await RunProjectOperationAsync("이전 번역 연결 해제", async cancellationToken =>
        {
            var db = _projectState.Db;
            if (db == null)
            {
                return;
            }

            await db.ClearPreviousTranslationsAsync(cancellationToken);
            if (!ReferenceEquals(db, _projectState.Db))
            {
                return;
            }

            await RefreshPreviousTranslationsAsync(cancellationToken);
            StatusMessage = "이전 번역 연결을 해제했습니다. 다음 번역부터 참고하지 않습니다.";
        });
    }

    private async Task RefreshPreviousTranslationsAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null || _projectState.PluginDocument == null)
        {
            PreviousTranslationSummary = PreviousTranslationUnavailable;
            HasPreviousTranslation = false;
            return;
        }

        var byId = await db.GetPreviousTranslationsByStringIdAsync(cancellationToken);
        var source = await db.GetPreviousTranslationSourceAsync(cancellationToken);
        if (!ReferenceEquals(db, _projectState.Db))
        {
            return;
        }

        foreach (var entry in Entries)
        {
            entry.PreviousTranslation = byId.TryGetValue(entry.Id, out var text) ? text : null;
        }

        HasPreviousTranslation = byId.Count > 0;
        PreviousTranslationSummary = source is { } s && byId.Count > 0
            ? $"{s.FileName}에서 {byId.Count}행 연결됨"
            : "연결된 이전 번역 없음";
    }
}
