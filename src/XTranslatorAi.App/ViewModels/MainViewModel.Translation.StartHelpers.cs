using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// The global glossary, series TM and official-name index all come from the game's global DB. When it could
    /// not be opened (locked, damaged, out of disk), runs used to go ahead with none of them and nothing said.
    /// Opening is retried here, since a lock may have cleared; if it still fails the user decides.
    /// </summary>
    private async Task<bool> ConfirmTranslateWithoutGlobalDbAsync()
    {
        if (await _globalProjectDbService.GetOrCreateAsync(SelectedFranchise, CancellationToken.None) != null)
        {
            return true;
        }

        var error = _globalProjectDbService.GetLastOpenError(SelectedFranchise) ?? "원인을 알 수 없습니다.";
        var proceed = _uiInteractionService.ShowMessage(
            $"전체 용어집·시리즈 TM DB를 열지 못했습니다.\n\n{error}\n\n"
            + "계속하면 전체 용어집, 시리즈 TM, 공식 이름 색인 없이 번역합니다. 계속할까요?\n\n"
            + $"자세한 내용: {AppLog.PathForUser}",
            "전체 DB를 열 수 없음",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning,
            UiMessageBoxResult.No
        ) == UiMessageBoxResult.Yes;
        if (!proceed)
        {
            StatusMessage = "번역을 시작하지 않았습니다. 전체 용어집·시리즈 TM DB를 열 수 없습니다.";
        }

        return proceed;
    }

    /// <summary>Status line addition after opening a project whose game's global DB could not be opened.</summary>
    private string DescribeUnavailableGlobalDb()
    {
        if (_globalProjectDbService.IsOpen(SelectedFranchise))
        {
            return "";
        }

        var error = _globalProjectDbService.GetLastOpenError(SelectedFranchise);
        return $" 전체 용어집·시리즈 TM DB를 열지 못했습니다{(error == null ? "" : $"({error})")}. 번역을 시작할 때 다시 시도합니다.";
    }

    private bool TryValidateApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return true;
        }

        StatusMessage = "Gemini API 키를 먼저 설정하세요.";
        return false;
    }

    private async Task TryPreloadContextsAsync(CancellationToken cancellationToken)
    {
        if (EnableProjectContext && !string.IsNullOrWhiteSpace(ApiKey))
        {
            try
            {
                // Generate once so all batches share the same context.
                if (string.IsNullOrWhiteSpace(ProjectContextPreview))
                {
                    await GenerateProjectContextCoreAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // ignore (translation should still run)
            }
        }
    }

    private void BeginTranslationUiState()
    {
        IsTranslating = true;
        IsPaused = false;
        _resumeTcs = null;
        _inProgressSinceTranslationStart.Clear();
        StatusMessage = "번역 중...";
    }

    private async Task SaveProjectInfoAsync(CancellationToken cancellationToken = default)
    {
        var db = _projectState.Db;
        var xmlInfo = _projectState.XmlInfo;
        if (db == null || !_projectState.HasSource)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await db.UpsertProjectAsync(
            new ProjectInfo(
                Id: 1,
                InputXmlPath: _projectState.InputXmlPath ?? "",
                AddonName: _projectState.AddonName,
                Franchise: SelectedFranchise,
                SourceLang: SourceLang,
                DestLang: TargetLang,
                XmlVersion: xmlInfo?.Version ?? "",
                XmlHasBom: xmlInfo?.HasBom ?? false,
                XmlPrologLine: xmlInfo?.PrologLine ?? "",
                ModelName: SelectedModel,
                BasePromptText: BasePromptText,
                CustomPromptText: CustomPromptText,
                UseCustomPrompt: UseCustomPrompt,
                CreatedAt: now,
                UpdatedAt: now
            ),
            cancellationToken
        );
    }

    private async Task PrepareTranslationsForResumeAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        // Resume unfinished rows without discarding completed translations or manual edits.
        await db.ResetInProgressToPendingAsync(cancellationToken);
        foreach (var vm in Entries)
        {
            if (vm.Status == StringEntryStatus.InProgress)
            {
                vm.Status = StringEntryStatus.Pending;
                vm.ErrorMessage = null;
            }
        }

        RecountProgress();
    }

    private async Task<IReadOnlyList<long>> LoadPendingIdsAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return Array.Empty<long>();
        }

        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken);
        // The rows this run translates, which are the rows RecountProgress counts as 대기; each finished row
        // then takes one off.
        PendingCount = ids.Count;
        return ids;
    }
}
