# Qwen‑MT Provider Integration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add Qwen‑MT (DashScope) as a first-class translation engine in the WPF app with engine-specific API keys (multi-key + auto failover), Qwen‑MT native options (terms/domain/TM), and BOOK-friendly long-text handling that preserves line breaks.

**Architecture:** Introduce a `TranslationEngine` concept in Core and App settings. Add a `QwenMtClient` that calls DashScope’s OpenAI-compatible endpoint (Singapore: `dashscope-intl`). Route translation calls based on engine/model and apply Qwen‑MT `translation_options` (source/target/terms/domains/tm_list). Keep existing token/placeholder masking + validation and preserve book formatting by validating line breaks and falling back to stricter chunking when needed.

**Tech Stack:** .NET (WPF), CommunityToolkit.Mvvm, HttpClient, System.Text.Json, existing `TranslationService` pipeline, existing placeholder/token validation.

---

### Task 1: Add Core engine types + Qwen‑MT client

**Files:**
- Create: `src/XTranslatorAi.Core/Translation/TranslationEngine.cs`
- Create: `src/XTranslatorAi.Core/Translation/QwenMtClient.cs`
- Create: `src/XTranslatorAi.Core/Translation/QwenMtClient.Types.cs`

**Step 1: Write a failing unit test for Qwen client error handling**

Create `tests/XTranslatorAi.Tests/QwenMtClientTests.cs` with a fake `HttpMessageHandler` that returns a 429 and assert:
- throws an exception whose message contains `HTTP 429`

**Step 2: Run the test to verify it fails**

Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
Expected: FAIL (types not found)

**Step 3: Implement minimal Qwen‑MT client**

Implement:
- POST `https://dashscope-intl.aliyuncs.com/compatible-mode/v1/chat/completions`
- `Authorization: Bearer <apiKey>`
- JSON request: `{ model, messages:[{role:"user",content:text}], translation_options:{source_lang,target_lang,terms,domains,tm_list} }`
- Parse `choices[0].message.content`
- Non-2xx => throw with `HTTP <code>` in message

**Step 4: Run tests**

Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
Expected: PASS

---

### Task 2: Route translation calls in `TranslationService` for Qwen‑MT

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslateIdsRequest.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.TextRequests.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.Items.cs`

**Step 1: Add engine + Qwen options to request**

Add optional fields at the end:
- `TranslationEngine Engine = TranslationEngine.Gemini`
- `string? QwenMtDomains = null`
- `bool QwenMtEnableTerms = true`
- `bool QwenMtEnableTranslationMemory = true`
- `bool QwenMtPreserveLineBreaks = true`

**Step 2: Add a failing test verifying Qwen path is used**

Create `tests/XTranslatorAi.Tests/TranslationServiceQwenMtSmokeTests.cs`:
- seed a db row
- use a Qwen handler that returns a known translation
- call TranslateIdsAsync with `Engine=QwenMt`, `ModelName=qwen-mt-flash`
- assert row becomes `Done` and `DestText` equals expected

**Step 3: Implement Qwen routing**

Implement in `TranslationService`:
- Accept optional `QwenMtClient` in ctor; store `_qwenMt`
- When `request.Engine == QwenMt` (or model starts with `qwen-mt-`):
  - Disable unsupported features (prompt cache, session-term seeding) by short-circuiting/guarding
  - Ensure worker path runs single-row translations (BatchSize=1 recommended from App)
  - In text request execution, call `_qwenMt.TranslateAsync(...)` with `translation_options`

**Step 4: Preserve line breaks for BOOK**

Add validation that output retains:
- `[pagebreak]` occurrences count
- newline count (or normalized `\n` count)
If mismatch and `QwenMtPreserveLineBreaks=true`, fall back to translating per-line/per-paragraph and re-join with original separators.

**Step 5: Run tests**

Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
Expected: PASS

---

### Task 3: Add App settings for engine + DashScope keys + Qwen options

**Files:**
- Modify: `src/XTranslatorAi.App/Services/AppSettingsStore.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`

**Step 1: Persist engine selection + DashScope keys**

Add to `AppSettings`:
- `translationEngine`
- `dashScopeApiKey`
- `dashScopeApiKeys[]`
- `qwenMtDomains`

**Step 2: Wire into ViewModel**

Add VM properties:
- `SelectedTranslationEngine`
- `DashScopeApiKey`
- `SavedDashScopeApiKeys`
- `QwenMtDomains`

**Step 3: Run build**

Run: `~/.dotnet/dotnet build XTranslatorAi.sln -c Release -p:EnableWindowsTargeting=true`
Expected: OK

---

### Task 4: Update WPF UI for engine selection + model costs

**Files:**
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Tools.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.StartHelpers.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.Run.cs`

**Step 1: Add Engine combobox**

Add `Engine:` selector near `Model:`. When `Qwen‑MT`:
- `AvailableModels` becomes fixed list (flash/plus/lite) with cost label
- Key UI points to DashScope key store

**Step 2: Pass Qwen options into translation request**

When engine=Qwen‑MT:
- Use DashScope api key (selected/current)
- Force `BatchSize=1` (or small)
- Disable prompt cache
- Pass `Engine=QwenMt` and `QwenMtDomains` etc

**Step 3: Run tests**

Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
Expected: PASS

---

### Task 5: Publish Windows single-file build

**Files:**
- (no code changes)

**Step 1: Publish**

Run:
`~/.dotnet/dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-win-x64-single`

Expected: `artifacts/publish-win-x64-single/TulliusTranslator.exe`

