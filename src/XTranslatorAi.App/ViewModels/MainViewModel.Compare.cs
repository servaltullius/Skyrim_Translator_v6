using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    public string CompareSelectedEntrySummary => BuildCompareSelectedEntrySummary();

    [ObservableProperty] private bool _compareIncludeProjectGlossary = true;
    [ObservableProperty] private bool _compareIncludeGlobalGlossary = true;
    [ObservableProperty] private bool _compareIncludeGlobalTranslationMemory = true;

    [ObservableProperty] private string _compare1Model = "gemini-3-flash-preview";
    [ObservableProperty] private bool _compare1ThinkingOff;
    [ObservableProperty] private string _compare1Status = "";
    [ObservableProperty] private string _compare1Output = "";
    [ObservableProperty] private bool _compare1IsRunning;

    [ObservableProperty] private string _compare2Model = "gemini-3-flash-preview";
    [ObservableProperty] private bool _compare2ThinkingOff = true;
    [ObservableProperty] private string _compare2Status = "";
    [ObservableProperty] private string _compare2Output = "";
    [ObservableProperty] private bool _compare2IsRunning;

    [ObservableProperty] private string _compare3Model = "gemini-2.5-flash-lite";
    [ObservableProperty] private bool _compare3ThinkingOff;
    [ObservableProperty] private string _compare3Status = "";
    [ObservableProperty] private string _compare3Output = "";
    [ObservableProperty] private bool _compare3IsRunning;

    public string[] CompareGeminiModelCandidates { get; } =
    {
        "gemini-3-flash-preview",
        "gemini-3-flash",
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
    };

    public System.Collections.IEnumerable Compare1AvailableModels => GetCompareGeminiModels();
    public System.Collections.IEnumerable Compare2AvailableModels => GetCompareGeminiModels();
    public System.Collections.IEnumerable Compare3AvailableModels => GetCompareGeminiModels();

    private System.Collections.IEnumerable GetCompareGeminiModels()
    {
        if (_modelInfoByName.Count > 0)
        {
            return _modelInfoByName.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        return CompareGeminiModelCandidates;
    }

    partial void OnSelectedEntryChanged(StringEntryViewModel? value)
    {
        _ = value;
        OnPropertyChanged(nameof(CompareSelectedEntrySummary));
    }

    private static string BuildCompareSelectedEntrySummary(StringEntryViewModel? entry)
    {
        if (entry == null)
        {
            return "선택된 항목 없음 (Strings 탭에서 행을 선택하세요)";
        }

        var rec = string.IsNullOrWhiteSpace(entry.Rec) ? "(REC 없음)" : entry.Rec.Trim();
        var edid = string.IsNullOrWhiteSpace(entry.Edid) ? "(EDID 없음)" : entry.Edid.Trim();
        return $"#{entry.OrderIndex} · {rec} · {edid}";
    }

    private string BuildCompareSelectedEntrySummary() => BuildCompareSelectedEntrySummary(SelectedEntry);

    [RelayCommand]
    private async Task RunCompare1Async() => await RunCompareSlotAsync(slot: 1);

    [RelayCommand]
    private async Task RunCompare2Async() => await RunCompareSlotAsync(slot: 2);

    [RelayCommand]
    private async Task RunCompare3Async() => await RunCompareSlotAsync(slot: 3);

    [RelayCommand]
    private async Task RunCompareAllAsync()
    {
        await RunCompareSlotAsync(slot: 1);
        await RunCompareSlotAsync(slot: 2);
        await RunCompareSlotAsync(slot: 3);
    }

    [RelayCommand]
    private void ClearCompareOutputs()
    {
        Compare1Status = "";
        Compare1Output = "";
        Compare2Status = "";
        Compare2Output = "";
        Compare3Status = "";
        Compare3Output = "";
    }

    private async Task RunCompareSlotAsync(int slot)
    {
        if (IsCompareSlotRunning(slot))
        {
            return;
        }

        if (!TryCreateCompareExecutionContext(slot, out var context))
        {
            return;
        }

        SetCompareIsRunning(slot, true);
        SetCompareStatus(slot, "Running...");
        try
        {
            var globalGlossary = CompareIncludeGlobalGlossary ? await TryLoadGlobalGlossaryAsync() : null;
            var globalTranslationMemory = CompareIncludeGlobalTranslationMemory ? await TryLoadGlobalTranslationMemoryAsync() : null;
            var request = BuildCompareRequest(context, globalGlossary, globalTranslationMemory);

            var result = await _compareTranslationService.RunAsync(request, CancellationToken.None);
            ApplyCompareResult(slot, result);
        }
        catch (Exception ex)
        {
            var user = UserFacingErrorClassifier.Classify(ex);
            SetCompareOutput(slot, ex.ToString());
            SetCompareStatus(slot, $"{user.Code}: {user.Message}");
        }
        finally
        {
            SetCompareIsRunning(slot, false);
        }
    }

    private CompareTranslationService.Request BuildCompareRequest(
        CompareExecutionContext context,
        IReadOnlyList<GlossaryEntry>? globalGlossary,
        IReadOnlyDictionary<string, string>? globalTranslationMemory
    )
    {
        var maxOut = ComputeMaxOutputTokens(context.ModelName);

        return new CompareTranslationService.Request(
            GeminiClient: _geminiClient,
            ProjectDb: _projectState.Db,
            ApiKey: context.ApiKey,
            ModelName: context.ModelName,
            ThinkingOff: context.ThinkingOff,
            SourceLang: SourceLang.Trim(),
            TargetLang: TargetLang.Trim(),
            SystemPrompt: BuildSystemPrompt(),
            SourceText: context.SourceText,
            Edid: context.Entry.Edid,
            Rec: context.Entry.Rec,
            BatchSize: BatchSize,
            MaxChars: MaxCharsPerBatch,
            Parallel: MaxParallelRequests,
            MaxOutputTokens: maxOut,
            UseRecStyleHints: UseRecStyleHints,
            EnableRepairPass: EnableRepairPass,
            EnableSessionTermMemory: EnableSessionTermMemory,
            SemanticRepairMode: SemanticRepairMode,
            EnableTemplateFixer: EnableTemplateFixer,
            KeepSkyrimTagsRaw: KeepSkyrimTagsRaw,
            EnableDialogueContextWindow: EnableDialogueContextWindow,
            EnablePromptCache: EnablePromptCache,
            EnableRiskyCandidateRerank: EnableRiskyCandidateRerank,
            RiskyCandidateCount: RiskyCandidateCount,
            IncludeProjectGlossary: CompareIncludeProjectGlossary,
            GlobalGlossary: globalGlossary,
            GlobalTranslationMemory: globalTranslationMemory
        );
    }

    private void ApplyCompareResult(int slot, CompareTranslationService.Result result)
    {
        if (!result.HasRow)
        {
            SetCompareStatus(slot, "결과를 읽지 못했습니다.");
            return;
        }

        if (result.Status == StringEntryStatus.Done)
        {
            SetCompareOutput(slot, result.DestText);
            SetCompareStatus(slot, result.TranslationMemoryHit ? "Done (TM)" : "Done");
            return;
        }

        if (result.Status == StringEntryStatus.Error)
        {
            var raw = result.ErrorMessage ?? "Unknown error";
            var classified = UserFacingErrorClassifier.ClassifyErrorMessage(raw);
            var statusText = classified.Code == "E000" ? "Error" : $"{classified.Code}: {classified.Message}";
            SetCompareOutput(slot, raw);
            SetCompareStatus(slot, statusText);
            return;
        }

        SetCompareOutput(slot, result.DestText);
        SetCompareStatus(slot, result.Status.ToString());
    }

    private bool IsCompareSlotRunning(int slot)
    {
        return (slot == 1 && Compare1IsRunning) || (slot == 2 && Compare2IsRunning) || (slot == 3 && Compare3IsRunning);
    }

    private bool TryCreateCompareExecutionContext(int slot, out CompareExecutionContext context)
    {
        context = default!;

        if (IsTranslating)
        {
            SetCompareStatus(slot, "번역 중에는 Compare 실행을 잠시 멈춰주세요.");
            return false;
        }

        var entry = SelectedEntry;
        if (entry == null)
        {
            SetCompareStatus(slot, "Strings 탭에서 비교할 행을 먼저 선택하세요.");
            return false;
        }

        var apiKey = (ApiKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetCompareStatus(slot, "Gemini API 키를 먼저 설정하세요.");
            return false;
        }

        var sourceText = entry.SourceText ?? "";
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            SetCompareStatus(slot, "원문(Source)이 비어있습니다.");
            return false;
        }

        var modelName = slot switch
        {
            1 => Compare1Model,
            2 => Compare2Model,
            3 => Compare3Model,
            _ => "",
        };
        var thinkingOff = slot switch
        {
            1 => Compare1ThinkingOff,
            2 => Compare2ThinkingOff,
            3 => Compare3ThinkingOff,
            _ => false,
        };

        var trimmedModel = (modelName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmedModel))
        {
            SetCompareStatus(slot, "모델명을 선택/입력하세요.");
            return false;
        }

        context = new CompareExecutionContext(entry, apiKey, sourceText, trimmedModel, thinkingOff);
        return true;
    }

    private sealed record CompareExecutionContext(
        StringEntryViewModel Entry,
        string ApiKey,
        string SourceText,
        string ModelName,
        bool ThinkingOff
    );

    private void SetCompareIsRunning(int slot, bool isRunning)
    {
        switch (slot)
        {
            case 1:
                Compare1IsRunning = isRunning;
                break;
            case 2:
                Compare2IsRunning = isRunning;
                break;
            case 3:
                Compare3IsRunning = isRunning;
                break;
        }
    }

    private void SetCompareStatus(int slot, string status)
    {
        switch (slot)
        {
            case 1:
                Compare1Status = status ?? "";
                break;
            case 2:
                Compare2Status = status ?? "";
                break;
            case 3:
                Compare3Status = status ?? "";
                break;
        }
    }

    private void SetCompareOutput(int slot, string output)
    {
        switch (slot)
        {
            case 1:
                Compare1Output = output ?? "";
                break;
            case 2:
                Compare2Output = output ?? "";
                break;
            case 3:
                Compare3Output = output ?? "";
                break;
        }
    }
}
