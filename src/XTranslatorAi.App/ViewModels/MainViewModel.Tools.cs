using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void ApplyFreeTierPreset()
    {
        // No API calls here: apply values and let the user Refresh models when needed.
        SelectedModel = PickModelCandidate(
            candidates: new[]
            {
                "gemini-2.5-flash-lite",
                "gemini-2.5-flash-lite-preview-09-2025",
            },
            fallback: "gemini-2.5-flash-lite"
        );

        EnablePromptCache = false;
        BatchSize = 8;
        MaxCharsPerBatch = 15000;
        MaxParallelRequests = 1;
        MaxOutputTokensOverride = 0; // Auto

        EnableBookFullModelOverride = true;
        BookFullModel = PickModelCandidate(
            candidates: new[]
            {
                "gemini-3-flash-preview",
                "gemini-3.0-flash-preview",
                "gemini-3-flash",
            },
            fallback: "gemini-3-flash-preview"
        );

        EnableQualityEscalation = true;
        QualityEscalationModel = PickModelCandidate(
            candidates: new[]
            {
                "gemini-2.5-flash",
                "gemini-2.5-flash-preview-09-2025",
            },
            fallback: "gemini-2.5-flash"
        );

        StatusMessage = "무료 티어 프리셋을 적용했습니다.";
    }

    [RelayCommand]
    private void ApplyPaidPreset()
    {
        // No API calls here: apply values and let the user Refresh models when needed.
        SelectedModel = PickModelCandidate(
            candidates: new[]
            {
                "gemini-3-flash-preview",
                "gemini-3.0-flash-preview",
                "gemini-3-flash",
            },
            fallback: "gemini-3-flash-preview"
        );

        // Paid tier: optimize request count without pushing concurrency too hard.
        // - Larger maxChars reduces long-text chunking (fewer requests) for book-like entries.
        // - Moderate batch size reduces request count for short strings while keeping JSON batches stable.
        EnablePromptCache = true;
        BatchSize = 12;
        MaxCharsPerBatch = 15000;
        MaxParallelRequests = 2;
        MaxOutputTokensOverride = 0; // Auto

        StatusMessage = "유료 프리셋을 적용했습니다. (3.0 Flash Preview + batch=12 / maxChars=15000 / parallel=2 / maxOut=auto)";
    }

    [RelayCommand]
    private void OpenGlossaryFolder()
    {
        try
        {
            var globalDbPath = ProjectPaths.GetGlobalGlossaryDbPath();
            var globalDir = Path.GetDirectoryName(Path.GetFullPath(globalDbPath));
            var rootDir = string.IsNullOrWhiteSpace(globalDir) ? null : Path.GetDirectoryName(globalDir);
            rootDir ??= globalDir ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (!_uiInteractionService.TryOpenFolder(rootDir))
            {
                throw new InvalidOperationException("Failed to open glossary folder.");
            }

            StatusMessage = "Opened glossary folder.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Open folder", ex);
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "Gemini API 키를 먼저 설정하세요.";
            return;
        }

        try
        {
            StatusMessage = "모델 목록을 불러오는 중...";
            var models = await _geminiClient.ListModelsAsync(ApiKey.Trim(), CancellationToken.None);
            ApplyAvailableModels(models);

            OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigSummary));
            OnPropertyChanged(nameof(EffectiveGeminiTranslationConfigToolTip));
            OnPropertyChanged(nameof(Compare1AvailableModels));
            OnPropertyChanged(nameof(Compare2AvailableModels));
            OnPropertyChanged(nameof(Compare3AvailableModels));
            StatusMessage = "모델 목록이 업데이트되었습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("모델 목록", ex);
        }
    }

    private void ApplyAvailableModels(IReadOnlyList<GeminiModel> models)
    {
        _modelInfoByName.Clear();
        foreach (var m in models)
        {
            if (!TryGetUsableModelName(m, out var modelName))
            {
                continue;
            }

            _modelInfoByName[modelName] = m;
        }

        var names = _modelInfoByName.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            return;
        }

        AvailableModels.ReplaceAll(names);
        if (!names.Contains(SelectedModel, StringComparer.Ordinal))
        {
            SelectedModel = PickPreferredModelName(names);
        }

        if (!names.Contains(BookFullModel, StringComparer.Ordinal))
        {
            BookFullModel = PickPreferredBookFullModelName(names, SelectedModel);
        }

        if (!names.Contains(QualityEscalationModel, StringComparer.Ordinal))
        {
            QualityEscalationModel = PickPreferredQualityEscalationModelName(names);
        }
    }

    private string PickModelCandidate(IReadOnlyList<string> candidates, string fallback)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c))
            {
                continue;
            }

            if (AvailableModels.Contains(c))
            {
                return c;
            }
        }

        return fallback;
    }

    private static bool TryGetUsableModelName(GeminiModel model, out string modelName)
    {
        modelName = "";

        var name = model.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (model.SupportedGenerationMethods is { Count: > 0 }
            && !model.SupportedGenerationMethods.Contains("generateContent", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        modelName = name.Replace("models/", "", StringComparison.Ordinal);
        return !string.IsNullOrWhiteSpace(modelName);
    }

    private static string PickPreferredModelName(IReadOnlyList<string> names)
    {
        var candidates = new[]
        {
            "gemini-2.5-flash-lite",
            "gemini-2.5-flash-lite-preview-09-2025",
            "gemini-2.5-flash",
            "gemini-2.5-flash-preview-09-2025",
            "gemini-3.0-flash-preview",
            "gemini-3-flash-preview",
        };

        foreach (var candidate in candidates)
        {
            if (names.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        return names[0];
    }

    private static string PickPreferredBookFullModelName(IReadOnlyList<string> names, string primaryModel)
    {
        var candidates = new[]
        {
            "gemini-3-flash-preview",
            "gemini-3.0-flash-preview",
            "gemini-3-flash",
        };

        foreach (var candidate in candidates)
        {
            if (names.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        return primaryModel;
    }

    private static string PickPreferredQualityEscalationModelName(IReadOnlyList<string> names)
    {
        var candidates = new[]
        {
            "gemini-2.5-flash",
            "gemini-2.5-flash-preview-09-2025",
        };

        foreach (var candidate in candidates)
        {
            if (names.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }

        return names[0];
    }

    [RelayCommand(CanExecute = nameof(CanEstimateCost))]
    private async Task EstimateCostAsync()
    {
        if (!TryGetCostEstimateContext(out var db, out var apiKey))
        {
            return;
        }

        if (!TryGetEstimateScope(out var includeCompletedItems))
        {
            return;
        }

        if (!TryGetEstimateSampleOption(out var runSample))
        {
            return;
        }

        var estimateRequest = await BuildCostEstimateRequestAsync(apiKey, includeCompletedItems, runSample);
        await RunCostEstimateAsync(db, estimateRequest);
    }

    private bool CanEstimateCost() => IsProjectLoaded && !IsTranslating;

    private bool TryGetCostEstimateContext(out ProjectDb db, out string apiKey)
    {
        var projectDb = _projectState.Db;
        if (projectDb == null || _projectState.XmlInfo == null)
        {
            db = null!;
            apiKey = "";
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            StatusMessage = "Gemini API 키를 먼저 설정하세요.";
            db = null!;
            apiKey = "";
            return false;
        }

        db = projectDb;
        apiKey = ApiKey.Trim();
        return true;
    }

    private bool TryGetEstimateScope(out bool includeCompletedItems)
    {
        var choice = _uiInteractionService.ShowMessage(
            "비용을 어떤 범위로 추정할까요?\n\n- Yes: 전체 항목(이미 번역된 항목 포함)\n- No: 남은 항목(Pending/Error)\n- Cancel: 취소",
            "비용 추정",
            UiMessageBoxButton.YesNoCancel,
            UiMessageBoxImage.Question
        );
        if (choice == UiMessageBoxResult.Cancel)
        {
            includeCompletedItems = false;
            return false;
        }

        includeCompletedItems = choice == UiMessageBoxResult.Yes;
        return true;
    }

    private bool TryGetEstimateSampleOption(out bool runSample)
    {
        var choice = _uiInteractionService.ShowMessage(
            "출력 토큰 추정을 위해 소량의 샘플 번역을 실행할까요?\n\n- Yes: 더 정확한 출력/비용 추정 (소량의 API 비용 발생)\n- No: 빠른 추정 (출력 토큰은 범위로만 표시)\n\n※ 대용량 프로젝트에서는 Yes가 오래 걸릴 수 있습니다.",
            "비용 추정",
            UiMessageBoxButton.YesNoCancel,
            UiMessageBoxImage.Question,
            UiMessageBoxResult.No
        );
        if (choice == UiMessageBoxResult.Cancel)
        {
            runSample = false;
            return false;
        }

        runSample = choice == UiMessageBoxResult.Yes;
        return true;
    }

    private async Task<TranslationCostEstimateRequest> BuildCostEstimateRequestAsync(
        string apiKey,
        bool includeCompletedItems,
        bool runSample
    )
    {
        var systemPrompt = BuildEffectiveSystemPrompt();
        var batchSize = Math.Clamp(BatchSize, 1, 100);
        var maxChars = Math.Clamp(MaxCharsPerBatch, 1000, 50000);

        var selectedModel = SelectedModel.Trim();
        var maxOut = ComputeMaxOutputTokens(selectedModel);

        return new TranslationCostEstimateRequest(
            ApiKey: apiKey,
            ModelName: selectedModel,
            SourceLang: SourceLang.Trim(),
            TargetLang: TargetLang.Trim(),
            SystemPrompt: systemPrompt,
            BatchSize: batchSize,
            MaxChars: maxChars,
            MaxOutputTokens: maxOut,
            RunSampleToEstimateOutputTokens: runSample,
            IncludeCompletedItems: includeCompletedItems,
            GlobalGlossary: await TryLoadGlobalGlossaryAsync(),
            KeepSkyrimTagsRaw: KeepSkyrimTagsRaw
        );
    }

    private string BuildEffectiveSystemPrompt()
        => UseCustomPrompt && !string.IsNullOrWhiteSpace(CustomPromptText)
            ? BasePromptText + "\n\n" + CustomPromptText
            : BasePromptText;

    private async Task RunCostEstimateAsync(ProjectDb db, TranslationCostEstimateRequest estimateRequest)
    {
        StatusMessage = "토큰/비용을 추정하는 중...";
        try
        {
            var estimator = new TranslationCostEstimator(db, _geminiClient);
            var estimate = await estimator.EstimateAsync(estimateRequest, CancellationToken.None);

            _uiInteractionService.ShowMessage(
                estimate.ToHumanReadableString(),
                "비용 추정",
                UiMessageBoxButton.Ok,
                UiMessageBoxImage.Information
            );
            LastCostEstimateSummary = BuildCostSummaryLine(estimate, estimateRequest.ModelName);
            StatusMessage = "비용 추정이 완료되었습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("비용 추정", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanFixMagDurPlaceholders))]
    private async Task FixMagDurPlaceholdersAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        StatusMessage = "플레이스홀더(<mag>/<dur>) 검수·교정 중...";
        try
        {
            var updates = new System.Collections.Generic.List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>();
            foreach (var vm in Entries)
            {
                if (vm.Status != StringEntryStatus.Done && vm.Status != StringEntryStatus.Edited)
                {
                    continue;
                }

                var fixedText = MagDurPlaceholderFixer.Fix(vm.SourceText, vm.DestText, TargetLang);
                if (string.Equals(fixedText, vm.DestText, StringComparison.Ordinal))
                {
                    continue;
                }

                vm.DestText = fixedText;
                updates.Add((vm.Id, fixedText, vm.Status, vm.ErrorMessage));
            }

            await db.UpdateStringTranslationsAsync(updates, CancellationToken.None);
            StatusMessage = updates.Count == 0 ? "교정할 항목이 없습니다." : $"교정 완료: {updates.Count}개 항목을 수정했습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("태그 교정", ex);
        }
    }

    private bool CanFixMagDurPlaceholders() => IsProjectLoaded && !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanReapplyPostEdits))]
    private async Task ReapplyPostEditsAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        StatusMessage = "후처리(플레이스홀더/단위/조사) 재적용 중...";
        try
        {
            var updates = new System.Collections.Generic.List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>();
            foreach (var vm in Entries)
            {
                if (vm.Status != StringEntryStatus.Done && vm.Status != StringEntryStatus.Edited)
                {
                    continue;
                }

                var sourceText = vm.SourceText ?? "";
                var destText = vm.DestText ?? "";
                if (string.IsNullOrWhiteSpace(destText))
                {
                    continue;
                }

                var fixedText = TranslationPostEdits.Apply(TargetLang, sourceText, destText, EnableTemplateFixer);
                if (string.Equals(fixedText, destText, StringComparison.Ordinal))
                {
                    continue;
                }

                vm.DestText = fixedText;
                updates.Add((vm.Id, fixedText, vm.Status, vm.ErrorMessage));
            }

            await db.UpdateStringTranslationsAsync(updates, CancellationToken.None);
            StatusMessage = updates.Count == 0 ? "교정할 항목이 없습니다." : $"후처리 재적용 완료: {updates.Count}개 항목을 수정했습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("후처리 재적용", ex);
        }
    }

    private bool CanReapplyPostEdits() => IsProjectLoaded && !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanImportGlobalTranslationMemory))]
    private async Task ImportGlobalTranslationMemoryAsync()
    {
        if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
        {
            StatusMessage = "Global DB 초기화에 실패했습니다.";
            return;
        }

        var filePath = _uiInteractionService.ShowOpenFileDialog(
            new OpenFileDialogRequest(
                Filter: "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
                Title: "Import translation memory (TSV: Source<TAB>Target)"
            )
        );
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await ImportGlobalTranslationMemoryFromTsvPathAsync(filePath, reloadAfterImport: false);
    }

    private bool CanImportGlobalTranslationMemory() => !IsTranslating;

    private static string BuildCostSummaryLine(TranslationCostEstimate estimate, string selectedModel)
    {
        if (estimate.CostEstimates.Count == 0)
        {
            return "";
        }

        var model = estimate.CostEstimates.FirstOrDefault(c => string.Equals(c.ModelName, selectedModel, StringComparison.OrdinalIgnoreCase))
                    ?? estimate.CostEstimates[0];

        return $"{estimate.ScopeLabel} · {model.ModelName} · 추정 ${model.TotalCostUsdLowWithPromptCache:0.###}~${model.TotalCostUsdHighWithPromptCache:0.###} (프롬프트 캐시)";
    }
}
