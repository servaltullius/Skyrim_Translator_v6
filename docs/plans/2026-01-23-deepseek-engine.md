# DeepSeek Engine Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a DeepSeek translation engine to the WPF app (Engine selector + API key + API Logs + cost display), using DeepSeek’s OpenAI-compatible Chat Completions API.

**Architecture:** Implement `DeepSeekClient` in Core as an OpenAI-compatible `/chat/completions` caller (base URL default `https://api.deepseek.com`). Extend `TranslationEngine` and route DeepSeek through the existing *single-row* text-only pipeline (no batch JSON schema) to keep output robust for Skyrim/xTranslator token contracts. Add app-side settings/UI for DeepSeek API key (saved profiles), static model list (`deepseek-chat`, `deepseek-reasoner`), and API log + per-call cost estimation using published token pricing.

**Tech Stack:** .NET, WPF, SQLite, HttpClient, System.Text.Json, CommunityToolkit.Mvvm.

---

### Task 1: Add failing smoke test (DeepSeek engine)

**Files:**
- Create: `tests/XTranslatorAi.Tests/TranslationServiceDeepSeekSmokeTests.cs`

**Step 1: Write the failing test**

Create a test that:
- Seeds a temporary `ProjectDb` with one Pending string
- Uses a custom `HttpMessageHandler` that only accepts `POST https://api.deepseek.com/chat/completions` and returns a minimal valid DeepSeek response JSON with `choices[0].message.content`
- Runs `TranslationService.TranslateIdsAsync()` with `Engine = TranslationEngine.DeepSeek` and `ModelName = "deepseek-chat"`
- Asserts the row becomes `Done` and the translation contains expected marker text

**Step 2: Run test to verify it fails**

Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

Expected: FAIL (engine not implemented yet).

---

### Task 2: Core client + errors + logging types

**Files:**
- Create: `src/XTranslatorAi.Core/Translation/DeepSeekClient.cs`
- Create: `src/XTranslatorAi.Core/Translation/DeepSeekClient.Types.cs`
- Create: `src/XTranslatorAi.Core/Translation/DeepSeekException.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.ErrorHandling.cs`

**Step 1: Add DeepSeek call types**
- `DeepSeekCallOperation` (initial: `ChatCompletions`)
- `DeepSeekCallUsage` (prompt/completion/total tokens)
- `DeepSeekCallLogEntry` (duration/status/success/error/model/apiKeyMask/usage)
- `IDeepSeekCallLogger`

**Step 2: Implement `DeepSeekClient.ChatCompletionsAsync(...)`**
- Default base URL: `https://api.deepseek.com` (allow override)
- Endpoint: `POST {baseUrl}/chat/completions`
- Request: `{ model, messages, temperature, max_tokens, stream=false }`
- Response: parse `choices[0].message.content`
- On non-2xx: throw `DeepSeekHttpException` containing status and truncated body
- Always log success/failure via `IDeepSeekCallLogger`

**Step 3: Extend retry/credential detection**
- Treat `DeepSeekHttpException` similarly to Gemini/Qwen in:
  - rate-limit detection (429)
  - credential errors (401/403)
  - server errors (5xx)
  - retry-after parsing

---

### Task 3: TranslationService supports DeepSeek engine

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationEngine.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SplitFallback.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.TextRequests.cs`

**Step 1: Add `TranslationEngine.DeepSeek`**

**Step 2: Wire `DeepSeekClient` into `TranslationService`**
- Add optional ctor param and private field
- In `NormalizeRequestForEngine`, validate DeepSeek client exists when selected

**Step 3: Route batches to single-row for DeepSeek**
- In split fallback, handle `DeepSeek` like `QwenMt` (translate per-row)

**Step 4: Implement DeepSeek text-only prompt execution**
- In `TranslateUserPromptOnceAsync`, call DeepSeek chat completions when engine is DeepSeek

**Step 5: Run tests**
- Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: PASS

---

### Task 4: WPF app integration (engine selector, keys, models, logs, cost)

**Files:**
- Modify: `src/XTranslatorAi.App/Services/AppSettingsStore.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.ApiKeyProfiles.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.ApiKeysAndLogging.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Tools.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.StartHelpers.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.Run.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ModelPolicy.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml.cs`

**Step 1: Persist DeepSeek keys**
- Extend `AppSettings` to include `deepSeekApiKey` and `deepSeekApiKeys`
- Add VM properties:
  - `DeepSeekApiKey`, `SavedDeepSeekApiKeys`, `SelectedSavedDeepSeekApiKey`, `SavedDeepSeekApiKeyName`, `HasSavedDeepSeekApiKey`
- Add save/remove commands mirroring Gemini/DashScope

**Step 2: Engine models list**
- When engine is DeepSeek: `AvailableModels = ["deepseek-chat", "deepseek-reasoner"]`
- Default select: `deepseek-chat`

**Step 3: Translation request uses correct API key**
- `TryValidateApiKey()` checks DeepSeek key when DeepSeek engine selected
- `BuildTranslateIdsRequest` chooses `ApiKey` based on engine (Gemini/DashScope/DeepSeek)

**Step 4: API Logs + cost**
- Add `UiDeepSeekCallLogger` to capture DeepSeek calls
- Compute per-call cost from token usage using DeepSeek published pricing
- Add `SelectedModelCostSummary` support for DeepSeek

**Step 5: Build WPF project**
- Run: `~/.dotnet/dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`

---

### Task 5: Publish single-file

**Step 1: Publish**
Run:
`~/.dotnet/dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-win-x64-single`

**Step 2: Verify output**
- Confirm `artifacts/publish-win-x64-single/TulliusTranslator.exe` exists.

