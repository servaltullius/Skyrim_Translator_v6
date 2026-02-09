# Multi-Franchise Support (TES/Fallout/Starfield) + Remove Nexus Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add franchise selection (Elder Scrolls / Fallout / Starfield) with per-franchise embedded prompt/glossary + per-franchise global DB separation, and completely remove the Nexus feature/UI.

**Architecture:** Introduce a `BethesdaFranchise` enum (Core) and a `SelectedFranchise` property (App). Franchise drives (1) embedded base prompt selection, (2) embedded built-in glossary seed, and (3) global DB path. Persist franchise to the project DB (Project table) so reopening the same project auto-restores. Add best-effort auto-detection from `<Params><Addon>` for official master filenames only; otherwise default to the user selection.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite

---

### Task 1: Delete Nexus UI/tab

**Files:**
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Delete: `src/XTranslatorAi.App/Views/NexusTabView.xaml`
- Delete: `src/XTranslatorAi.App/Views/NexusTabView.xaml.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Tabs.cs`
- Delete: `src/XTranslatorAi.App/ViewModels/Tabs/NexusTabViewModel.cs`

**Steps:**
1. Remove the Nexus tab from `MainWindow.xaml`.
2. Remove the Nexus tab view + viewmodel wiring.
3. Build the app project to ensure XAML compiles.

Verify:
- Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`
- Expected: build succeeds

---

### Task 2: Remove Nexus settings + viewmodel properties/commands

**Files:**
- Modify: `src/XTranslatorAi.App/Services/AppSettingsStore.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Project.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.Prompt.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.StartHelpers.cs`
- Modify: `src/XTranslatorAi.App/Services/SystemPromptBuilder.cs`

**Steps:**
1. Delete `nexusApiKey` setting read/write and related UI state (`HasSavedNexusApiKey`).
2. Remove `EnableNexusContext`, `NexusModUrl`, `NexusContextPreview` and any commands referencing Nexus.
3. Update `SystemPromptBuilder` and prompt building to remove nexus injection.

Verify:
- Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`
- Expected: build succeeds

---

### Task 3: Introduce `BethesdaFranchise` enum (Core) + persist in project DB

**Files:**
- Create: `src/XTranslatorAi.Core/Models/BethesdaFranchise.cs`
- Modify: `src/XTranslatorAi.Core/Models/ProjectInfo.cs`
- Modify: `src/XTranslatorAi.Core/Data/ProjectDb.Core.cs`
- Modify: `src/XTranslatorAi.Core/Data/ProjectDb.Project.cs`
- Test: (optional) `tests/XTranslatorAi.Tests/ProjectDbProjectFranchiseTests.cs`

**Steps:**
1. Add `BethesdaFranchise` enum with values: `ElderScrolls`, `Fallout`, `Starfield`.
2. Add a nullable `Franchise` field to `ProjectInfo` and store it in the `Project` table (new column `Franchise`).
3. Add schema migration for existing DBs (ALTER TABLE if missing).
4. Add a small unit test that opens an in-memory DB, upserts a project with franchise, and reads it back.

Verify:
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: all tests pass

---

### Task 4: Add franchise selection UI + base prompt per franchise

**Files:**
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`
- Modify: `src/XTranslatorAi.App/XTranslatorAi.App.csproj`
- Add: `src/XTranslatorAi.App/Assets/메타프롬프트_폴아웃.md`
- Add: `src/XTranslatorAi.App/Assets/메타프롬프트_스타필드.md`

**Steps:**
1. Add a franchise ComboBox to the top toolbar.
2. When franchise changes, update `BasePromptText` to the embedded prompt for that franchise.
3. Keep the existing Skyrim prompt as the default for `ElderScrolls`.

Verify:
- Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`
- Expected: build succeeds

---

### Task 5: Per-franchise built-in glossary + per-franchise global DB path

**Files:**
- Modify: `src/XTranslatorAi.App/Services/ProjectPaths.cs`
- Modify: `src/XTranslatorAi.App/Services/GlobalProjectDbService.cs`
- Modify: `src/XTranslatorAi.App/Services/BuiltInGlossaryService.cs`
- Modify: `src/XTranslatorAi.App/Services/EmbeddedAssets.cs`
- Modify: `src/XTranslatorAi.App/Services/ProjectWorkspaceService.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Project.cs`
- Add: `src/XTranslatorAi.App/Assets/기본용어집_폴아웃.md`
- Add: `src/XTranslatorAi.App/Assets/기본용어집_스타필드.md`

**Steps:**
1. Change the global DB path to be per franchise; keep the current path for `ElderScrolls` for backward compatibility.
2. Seed built-in glossary from the franchise-specific embedded glossary file.
3. On `Open XML`, determine franchise:
   - If project DB already has a stored franchise → use it.
   - Else best-effort detect from `<Addon>` if it matches official master filenames.
   - Else use the currently selected franchise.
   Persist the chosen franchise to the project DB.

Verify:
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -p:EnableWindowsTargeting=true`
- Expected: all pass

