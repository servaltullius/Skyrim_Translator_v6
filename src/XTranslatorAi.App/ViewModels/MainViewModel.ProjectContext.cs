using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text.ProjectContext;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanUseProjectContextTools))]
    private Task GenerateProjectContextAsync() => RunProjectOperationAsync("문맥 생성", GenerateProjectContextCoreAsync);

    private async Task GenerateProjectContextCoreAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null || _projectState.XmlInfo == null)
        {
            return;
        }

        var apiKey = (ApiKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusMessage = "Gemini API 키를 먼저 설정하세요.";
            return;
        }

        try
        {
            StatusMessage = "Generating project context (scan + Gemini)...";

            var report = await BuildProjectContextScanReportAsync(cancellationToken);
            var translationPrompt = BuildProjectContextTranslationPrompt();
            var userPrompt = BuildProjectContextUserPrompt(report, translationPrompt);
            var request = BuildProjectContextGenerationRequest(userPrompt, isRetry: false);

            var raw = await _geminiClient.GenerateContentAsync(apiKey, SelectedModel.Trim(), request, cancellationToken);
            if (!ProjectContextResponseParser.TryParseContext(raw, out var ctx) || string.IsNullOrWhiteSpace(ctx))
            {
                StatusMessage = "Project context generation: invalid JSON output; retrying...";
                var retryRequest = BuildProjectContextGenerationRequest(userPrompt, isRetry: true);
                var retryRaw = await _geminiClient.GenerateContentAsync(apiKey, SelectedModel.Trim(), retryRequest, cancellationToken);
                if (!ProjectContextResponseParser.TryParseContext(retryRaw, out ctx) || string.IsNullOrWhiteSpace(ctx))
                {
                    throw new InvalidOperationException("Gemini response is missing 'context'.");
                }
            }

            ctx = TrimAndClamp(ctx, maxChars: 6000);

            await db.UpsertProjectContextAsync(ctx, cancellationToken);
            ProjectContextPreview = ctx;
            StatusMessage = "Project context updated.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetUserFacingError("Project context", ex);
        }
    }

    private string BuildProjectContextTranslationPrompt()
    {
        var translationPrompt = (BasePromptText ?? "").Trim();
        if (UseCustomPrompt && !string.IsNullOrWhiteSpace(CustomPromptText))
        {
            translationPrompt += "\n\n" + CustomPromptText.Trim();
        }

        return string.IsNullOrWhiteSpace(translationPrompt) ? "" : TrimAndClamp(translationPrompt, maxChars: 12000);
    }

    private GeminiGenerateContentRequest BuildProjectContextGenerationRequest(string userPrompt, bool isRetry)
    {
        var franchiseName = SelectedFranchise switch
        {
            BethesdaFranchise.ElderScrolls => "Skyrim",
            BethesdaFranchise.Fallout => "Fallout 4",
            BethesdaFranchise.Starfield => "Starfield",
            _ => "Bethesda game"
        };
        var systemPrompt =
            $"You are a Korean {franchiseName} mod localization expert.\n"
            + "You generate a concise, high-signal 'Project Context' section to improve translation consistency.\n"
            + "Output ONLY valid JSON. No markdown, no code fences, no explanations.\n"
            + (isRetry ? "IMPORTANT: Output must be a single JSON object with the key \"context\".\n" : "");

        return new GeminiGenerateContentRequest(
            Contents: new List<GeminiContent>
            {
                new(Role: "user", Parts: new List<GeminiPart> { new(userPrompt) }),
            },
            CachedContent: null,
            SystemInstruction: new GeminiContent(Role: null, Parts: new List<GeminiPart> { new(systemPrompt) }),
            GenerationConfig: new GeminiGenerationConfig(
                Temperature: isRetry ? 0.0 : 0.2,
                MaxOutputTokens: 4096,
                ResponseMimeType: "application/json",
                ResponseSchema: BuildProjectContextResponseSchema(),
                ThinkingConfig: null
            ),
            SafetySettings: new List<GeminiSafetySetting>
            {
                new("HARM_CATEGORY_HARASSMENT", "BLOCK_NONE"),
                new("HARM_CATEGORY_HATE_SPEECH", "BLOCK_NONE"),
                new("HARM_CATEGORY_SEXUALLY_EXPLICIT", "BLOCK_NONE"),
                new("HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE"),
            }
        );
    }

    [RelayCommand(CanExecute = nameof(CanUseProjectContextTools))]
    private async Task SaveProjectContextAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var text = (ProjectContextPreview ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "Project context가 비어있습니다.";
            return;
        }

        try
        {
            await db.UpsertProjectContextAsync(text, CancellationToken.None);
            StatusMessage = "Project context saved.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Project context save", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseProjectContextTools))]
    private async Task ClearProjectContextAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        try
        {
            await db.ClearProjectContextAsync(CancellationToken.None);
            ProjectContextPreview = "";
            StatusMessage = "Project context cleared.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Project context clear", ex);
        }
    }

    private bool CanUseProjectContextTools() => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive;

    private async Task ReloadProjectContextAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        try
        {
            var ctx = await db.TryGetProjectContextAsync(CancellationToken.None);
            ProjectContextPreview = ctx?.ContextText ?? "";
        }
        catch
        {
            // ignore
        }
    }
}
