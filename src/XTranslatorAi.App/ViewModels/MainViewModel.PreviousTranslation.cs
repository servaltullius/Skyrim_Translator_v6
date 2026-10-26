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
    [NotifyCanExecuteChangedFor(nameof(ImportPreviousSourceCommand))]
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
            // A translated release is written in the target encoding, with its Strings or BSA next to it.
            var options = document.Info.Options.ForOtherRelease() with
            {
                SourceEncoding = _projectState.PluginTargetEncoding ?? PluginTargetEncoding,
            };
            // An older Korean release saved in CP949 is read as CP949 (it was refused, and had to be saved again as UTF-8).
            var previous = await Task.Run(() => PluginReader.ReadTranslatedReleaseAsync(path, options, cancellationToken), cancellationToken);
            var asCp949 = previous.Info.Options.SourceEncoding == PluginReader.Cp949Encoding
                && !string.Equals(options.SourceEncoding, PluginReader.Cp949Encoding, StringComparison.OrdinalIgnoreCase);
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
            StatusMessage = $"이전 번역 {result.TextByFieldKey.Count}행을 연결했습니다({Path.GetFileName(path)}"
                + (asCp949 ? ", 옛 한국어판이라 CP949로 읽음" : "") + "). "
                + $"제외: 이전 판에 없음 {result.NotInPreviousRelease}, 레코드 구성이 바뀜 {result.ChangedRecord}, "
                + $"원문 그대로 {result.SameAsSource}, 번역 안 됨 {result.NotTranslated}. "
                + "다음 번역부터 참고합니다. 이미 번역된 행에 반영하려면 '다시 번역'을 쓰세요. "
                + "이전 번역판의 영어 원본도 있으면 '이전 영어판 연결'로 고르세요. 영어가 같은 행은 이전 번역을 그대로 씁니다.";
        });
    }

    /// <summary>
    /// Links the English release the earlier translation was made from. Where a field's English is the same in both
    /// releases, the earlier translation is reused as it is when translating, like xTranslator's SST dictionaries;
    /// where it changed, the earlier translation stays a reference for the model.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearPreviousTranslation))]
    private async Task ImportPreviousSourceAsync()
    {
        var document = _projectState.PluginDocument;
        if (document == null)
        {
            return;
        }

        var path = _uiInteractionService.ShowOpenFileDialog(new OpenFileDialogRequest(
            "Skyrim SE/AE 플러그인 (*.esp;*.esm;*.esl)|*.esp;*.esm;*.esl",
            "이전 번역판이 번역한 영어 원본 선택 (예: 이전 버전 영어판)"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await RunProjectOperationAsync("이전 영어판 연결", async cancellationToken =>
        {
            var db = _projectState.Db;
            if (db == null)
            {
                return;
            }

            StatusMessage = "이전 영어판을 읽는 중...";
            // The earlier English is read as the current English is, with its own Strings or BSA next to it, from the
            // chosen source encoding (the current one may be its fallback).
            var options = document.Info.Options.ForOtherRelease(PluginSourceEncoding);
            var previous = await Task.Run(() => PluginReader.ReadAsync(path, options, cancellationToken), cancellationToken);
            var result = PreviousTranslationMatcher.MatchSources(document.Fields, document.Info.Masters,
                previous.Fields, previous.Info.Masters);
            var name = Path.GetFileName(path);
            if (result.SourceByFieldKey.Count == 0)
            {
                StatusMessage = $"이전 영어판을 연결하지 못했습니다: {name}에서 같은 레코드를 찾지 못했습니다. 같은 모드의 이전 버전인지 확인하세요.";
                return;
            }

            // A translated release here would make every row "unchanged" against itself.
            if (LanguageHelper.IsKoreanLanguage(TargetLang)
                && result.WithHangul * 2 > result.SourceByFieldKey.Count)
            {
                StatusMessage = $"이전 영어판을 연결하지 못했습니다: {name}은 한국어로 번역된 파일입니다. 이전 번역판이 번역한 영어 원본을 고르세요.";
                return;
            }

            await db.ReplacePreviousSourcesAsync(name, result.SourceByFieldKey, cancellationToken);
            if (!ReferenceEquals(db, _projectState.Db))
            {
                return;
            }

            var reusable = await db.GetReusablePreviousTranslationsByStringIdAsync(cancellationToken);
            await RefreshPreviousTranslationsAsync(cancellationToken);
            StatusMessage = $"이전 영어판({name})과 비교했습니다. 영어가 같고 이전 번역이 있는 {reusable.Count}행은 "
                + "다음 번역 때 AI 없이 이전 번역을 그대로 씁니다. "
                + $"영어가 바뀐 {result.Changed}행은 이전 번역이 있으면 참고해 새로 번역합니다. "
                + $"(이전 판에 없음 {result.NotInPreviousRelease}, 레코드 구성이 바뀜 {result.ChangedRecord}) "
                + "이미 번역된 행에 반영하려면 '다시 번역'을 쓰세요.";
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
        var sourceFile = await db.GetPreviousSourceFileNameAsync(cancellationToken);
        var reusable = sourceFile == null
            ? new System.Collections.Generic.Dictionary<long, string>()
            : await db.GetReusablePreviousTranslationsByStringIdAsync(cancellationToken);
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
              + (sourceFile == null ? "" : $" · 영어판 {sourceFile}: 영어가 같은 {reusable.Count}행은 그대로 씀")
            : "연결된 이전 번역 없음";
    }
}
