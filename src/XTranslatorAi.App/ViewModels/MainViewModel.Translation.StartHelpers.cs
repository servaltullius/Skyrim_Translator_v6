using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
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
        StatusMessage = "Translating...";
    }

    private async Task SaveProjectInfoAsync(CancellationToken cancellationToken = default)
    {
        var db = _projectState.Db;
        var xmlInfo = _projectState.XmlInfo;
        if (db == null || xmlInfo == null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await db.UpsertProjectAsync(
            new ProjectInfo(
                Id: 1,
                InputXmlPath: _projectState.InputXmlPath ?? "",
                AddonName: xmlInfo.AddonName,
                Franchise: SelectedFranchise,
                SourceLang: SourceLang,
                DestLang: TargetLang,
                XmlVersion: xmlInfo.Version,
                XmlHasBom: xmlInfo.HasBom,
                XmlPrologLine: xmlInfo.PrologLine,
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
        DoneCount = 0;
        foreach (var vm in Entries)
        {
            if (vm.Status == StringEntryStatus.Done || vm.Status == StringEntryStatus.Edited)
            {
                DoneCount++;
                continue;
            }
            if (vm.Status == StringEntryStatus.InProgress)
            {
                vm.Status = StringEntryStatus.Pending;
                vm.ErrorMessage = null;
            }
        }
    }

    private async Task<IReadOnlyList<long>> LoadPendingIdsAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return Array.Empty<long>();
        }

        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending, StringEntryStatus.Error }, cancellationToken);
        PendingCount = ids.Count;
        return ids;
    }
}
