# Model Presets + 3-Model Compare Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** When the user selects a model, auto-apply sane per-model defaults (safety/throughput), and add a WPF “Compare” tab that runs the same string through 3 engine/model slots to compare outputs side-by-side.

**Architecture:** Implement a small per-model preset mapping in the WPF `MainViewModel`, gated by a user setting `EnableAutoModelPresets`. For comparison, add a new tab view that translates the currently selected string entry via 3 slots. Each slot runs `TranslationService.TranslateIdsAsync()` against a temporary SQLite `ProjectDb` seeded with the row (and optional glossary/TM), so we reuse the exact core pipeline and token-contract validation without touching the real project DB.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, SQLite (`ProjectDb`), existing `TranslationService` engines (Gemini/Qwen‑MT/DeepSeek).

---

### Task 1: Add auto preset toggle + persistence

**Files:**
- Modify: `src/XTranslatorAi.App/Services/AppSettingsStore.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**Steps:**
1. Add `enableAutoModelPresets` to settings and load/save it.
2. Add `EnableAutoModelPresets` observable property in `MainViewModel`.
3. Add a checkbox in “고급 설정 → 번역 설정” to toggle auto presets.

---

### Task 2: Apply per-model default settings on model selection

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ModelPolicy.cs`
- (Optional) Create: `src/XTranslatorAi.App/ViewModels/MainViewModel.ModelPresets.cs`

**Steps:**
1. Extend `OnSelectedModelChanged` to call `ApplyAutoPresetForSelectedModel()` when enabled.
2. Implement preset mapping (initial “safe defaults”):
   - Qwen‑MT models: `Batch=1`, `Parallel=1`, `MaxChars=6000~8000`, `KeepSkyrimTagsRaw=false`, `QwenMtPreserveLineBreaks=false`, `QwenMtMinDelayMs≈250`.
   - DeepSeek models: `Batch=1`, `Parallel=1`, `MaxChars≈8000`, `KeepSkyrimTagsRaw=false`.
   - Gemini models: keep existing user values (no override) for now to avoid surprises.

---

### Task 3: Add “Compare” tab (3 slots) using temp ProjectDb

**Files:**
- Create: `src/XTranslatorAi.App/Views/CompareTabView.xaml`
- Create: `src/XTranslatorAi.App/Views/CompareTabView.xaml.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Create: `src/XTranslatorAi.App/ViewModels/MainViewModel.Compare.cs`

**Steps:**
1. Add a new tab to the main `TabControl`: `Compare`.
2. Implement 3 slots:
   - Each slot: Engine combobox, model combobox, “Run” button, output textbox, status text.
   - “Run all” button to execute slots sequentially.
3. For each slot run:
   - Seed temp `ProjectDb` with the selected row’s `Rec/Edid/SourceText`.
   - Optionally seed merged glossary (project + global) into temp DB (toggle).
   - Call `TranslationService.TranslateIdsAsync()` with slot engine/model/key and read back Done/Error text.
4. Display output or a user-facing error code (e.g., E330) per slot.

---

### Task 4: Verify build + publish single-file

**Steps:**
1. Run tests: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
2. Build WPF: `~/.dotnet/dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`
3. Publish single-file:
   - `~/.dotnet/dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-win-x64-single`
4. Confirm output exists: `artifacts/publish-win-x64-single/TulliusTranslator.exe`

