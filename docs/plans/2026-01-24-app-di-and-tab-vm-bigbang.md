# App DI + Tab ViewModels Big-Bang Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Replace the current God-class WPF `MainViewModel` with a DI-created “shell + tab VMs + services” structure while keeping the UI/behavior stable.

**Architecture:** Introduce an explicit composition root in `App.xaml.cs`, create a small `MainViewModel` shell that composes tab-specific view models, and extract non-UI logic into services (project workspace, translation runner, prompt builder, logging). Tabs bind to their own view models via `DataContext` set from the shell.

**Tech Stack:** .NET 8 WPF (`net8.0-windows`), CommunityToolkit.Mvvm, existing Core services (`GeminiClient`, `TranslationService`, `ProjectDb`, `NexusModsClient`).

---

### Task 1: Create composition root (manual DI)

**Files:**
- Modify: `src/XTranslatorAi.App/App.xaml.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml.cs`

**Steps:**
1. Remove `Window.DataContext` from `MainWindow.xaml`.
2. Add `MainWindow(MainViewModel vm)` constructor and set `DataContext = vm`.
3. In `App.OnStartup`, construct dependencies (`HttpClient`, `AppSettingsStore`, `ApiLogsViewModel`, `GeminiClient`, `NexusModsClient`) and then create the shell `MainViewModel` and `MainWindow`.

**Verification:**
- Build/publish: `~/.dotnet/dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64-singlefile /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true /p:IncludeAllContentForSelfExtract=true /p:DebugType=None /p:DebugSymbols=false /p:EnableWindowsTargeting=true`

---

### Task 2: Introduce shell + tab VMs

**Files:**
- Create: `src/XTranslatorAi.App/ViewModels/Shell/MainViewModel.cs`
- Create: `src/XTranslatorAi.App/ViewModels/Tabs/*.cs` (one per tab)
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**Steps:**
1. Create a thin shell `MainViewModel` that only composes:
   - `SettingsViewModel` (top bar model + expander settings/tools + API keys)
   - `EntriesViewModel` (strings list + selection + dest save + glossary lookup)
   - `CompareTabViewModel`
   - `LqaTabViewModel`
   - `ProjectGlossaryTabViewModel`
   - `GlobalGlossaryTabViewModel`
   - `GlobalTranslationMemoryTabViewModel`
   - `PromptTabViewModel`
   - `ProjectContextTabViewModel`
   - `NexusTabViewModel`
   - `ApiLogsTabViewModel`
2. Update `MainWindow.xaml` TabItems so each `*TabView` sets `DataContext="{Binding <TabVmProperty>}"`.

**Verification:**
- App compiles and opens (manual smoke).

---

### Task 3: Extract services (A scope)

**Files:**
- Create: `src/XTranslatorAi.App/Services/ProjectWorkspaceService.cs`
- Create: `src/XTranslatorAi.App/Services/TranslationRunnerService.cs`
- Create: `src/XTranslatorAi.App/Services/SystemPromptBuilder.cs`

**Steps:**
1. Move XML load/export + DB lifecycle into `ProjectWorkspaceService`.
2. Move translation run orchestration (pause/cancel/failover hooks, row update pump) into `TranslationRunnerService` and have `EntriesViewModel` provide the minimal callbacks for UI updates.
3. Centralize prompt composition (`Base + Custom + ProjectContext + NexusContext`) into `SystemPromptBuilder`.

**Verification:**
- Existing Core tests still pass: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Publish still succeeds (same publish command).

---

### Task 4: Keep behavioral parity + regression checks

**Checklist (manual):**
- Open XML → strings load + counts correct
- Start/Pause/Stop translation works
- Compare tab still runs (including Thinking OFF toggles)
- LQA scan works
- Glossary tabs CRUD works
- Project context generate/save/clear works
- Nexus fetch/search works
- API logs show token/cost + totals
- “유료 프리셋” defaults unchanged (parallel=1)

---

### Task 5: Cleanup

**Steps:**
1. Delete legacy `MainViewModel.*` files after functionality is fully moved.
2. Run `python3 scripts/vibe.py agents lint` if needed.

