using System;
using System.Threading;
using System.Threading.Tasks;
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

    private async Task TryPreloadContextsAsync()
    {
        if (EnableProjectContext && !string.IsNullOrWhiteSpace(ApiKey))
        {
            try
            {
                // Generate once so all batches share the same context.
                if (string.IsNullOrWhiteSpace(ProjectContextPreview))
                {
                    await GenerateProjectContextAsync();
                }
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
        DoneCount = 0;
    }

    private async Task SaveProjectInfoAsync()
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
            CancellationToken.None
        );
    }

    private async Task ResetNonEditedTranslationsAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        // Always start fresh: discard previous AI translations (keep only manual edits).
        await db.ResetNonEditedTranslationsAsync(CancellationToken.None);
        await db.DeleteStringNotesByKindAsync(TmHitNoteKind, CancellationToken.None);
        foreach (var vm in Entries)
        {
            if (vm.Status == StringEntryStatus.Edited)
            {
                continue;
            }

            vm.Status = StringEntryStatus.Pending;
            vm.ErrorMessage = null;
            vm.DestText = "";
            vm.IsTranslationMemoryApplied = false;
        }
    }

    private async Task<IReadOnlyList<long>> LoadPendingIdsAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return Array.Empty<long>();
        }

        var ids = await db.GetStringIdsByStatusAsync(new[] { StringEntryStatus.Pending }, CancellationToken.None);
        PendingCount = ids.Count;
        return ids;
    }
}
