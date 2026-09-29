# Franchise TM Boundary Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reframe the current `Global TM` feature into a franchise-scoped translation memory system so Elder Scrolls data is treated as Skyrim/TES memory, Starfield memory can be added safely, and existing user data remains usable.

**Architecture:** Keep the existing SQLite `TranslationMemory` table and Elder Scrolls legacy DB path for backward compatibility, but change the application boundary from “global” to “franchise-scoped global.” User-facing labels become franchise-aware (`Skyrim/TES TM`, `Fallout TM`, `Starfield TM`), and application services/view-models are renamed or wrapped to reflect the real scope without changing matching behavior.

**Tech Stack:** .NET, WPF, CommunityToolkit.Mvvm, SQLite via existing `ProjectDb`, xUnit.

---

## Requirements Summary

- Preserve current TM hit behavior and precedence: project TM overrides franchise TM.
- Do not migrate or delete existing Elder Scrolls TM data on disk.
- Make Starfield/Fallout/TES boundaries explicit in UI, services, and status messages.
- Avoid introducing a new cross-franchise shared TM in this change.
- Keep compare flow, auto-import flow, and translation runner loading logic consistent with the new terminology.
- Update tests and operator-facing docs for the new scope.

## Non-Goals

- No TM quality scoring or fuzzy matching changes.
- No DB schema redesign.
- No import of Team Waldo Starfield data in this change.
- No rework of glossary behavior beyond terminology alignment where the UI already shares the same global DB service.

## File Map

**Primary behavior and naming**
- Modify: `src/XTranslatorAi.App/Services/GlobalTranslationMemoryService.cs`
  Responsibility: service boundary for franchise-scoped TM DB access.
- Modify: `src/XTranslatorAi.App/Services/GlobalProjectDbService.cs`
  Responsibility: franchise-scoped DB cache and selected-franchise resolution.
- Modify: `src/XTranslatorAi.App/Services/ProjectPaths.cs`
  Responsibility: legacy Elder Scrolls path compatibility and future-safe franchise path helpers.
- Modify: `src/XTranslatorAi.App/Services/TranslationRunnerService.cs`
  Responsibility: load franchise TM for translate runs.
- Modify: `src/XTranslatorAi.App/Services/CompareTranslationService.cs`
  Responsibility: use renamed franchise TM request field and keep compare behavior aligned.

**View-model and UI terminology**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
  Responsibility: reload on franchise switch, user-facing status refresh.
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
  Responsibility: host properties/commands currently named `GlobalTranslationMemory*`.
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalTranslationMemory.cs`
  Responsibility: franchise TM CRUD, import/export, status strings.
- Modify: `src/XTranslatorAi.App/ViewModels/Tabs/IGlobalTranslationMemoryTabHost.cs`
  Responsibility: tab host contract.
- Modify: `src/XTranslatorAi.App/ViewModels/Tabs/GlobalTranslationMemoryTabViewModel.cs`
  Responsibility: tab projection over renamed host properties.
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Compare.cs`
  Responsibility: compare tab checkbox/property naming alignment if changed.
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
  Responsibility: toolbar button text, tab header text.
- Modify: `src/XTranslatorAi.App/Views/GlobalTranslationMemoryTabView.xaml`
  Responsibility: in-tab labels and button strings.
- Modify: `src/XTranslatorAi.App/Views/CompareTabView.xaml`
  Responsibility: checkbox labels/tooltips for franchise TM.

**Core translation wiring**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslationMemory.cs`
  Responsibility: code comments and method parameter names to reflect franchise TM semantics.
- Modify: `src/XTranslatorAi.Core/Translation/TranslateIdsRequest.cs` or matching request record file if the request shape is defined elsewhere.
  Responsibility: rename `GlobalTranslationMemory` field if the public request contract is updated.

**Tests and docs**
- Modify: `tests/XTranslatorAi.Tests/TranslationMemoryTests.cs`
  Responsibility: rename test intent from global TM to franchise TM where applicable.
- Modify: `tests/XTranslatorAi.Tests/TranslationServiceTranslationMemoryValidationTests.cs`
  Responsibility: keep TM fallback/hit validation aligned after rename.
- Create: `tests/XTranslatorAi.Tests/FranchiseTranslationMemoryPathTests.cs`
  Responsibility: verify Elder Scrolls legacy path remains stable while Fallout/Starfield stay separated.
- Modify: `AGENTS.md`
  Responsibility: short repo rule clarifying that TM is franchise-scoped, not cross-game global.
- Modify: `README.md`
  Responsibility: describe TM scope accurately for users.

## Architecture Decision

**Recommendation:** Treat the existing feature as `Franchise TM` everywhere above the DB layer.

**Why this is the simplest viable architecture**
- The code already loads TM by selected franchise via `GlobalProjectDbService` and franchise-specific DB selection.
- Elder Scrolls uses a legacy generic path only for backward compatibility, not because the feature is truly cross-franchise.
- A new cross-franchise TM layer would add ambiguity, migration questions, and quality risk without a current use case.

**Rejected alternatives**
- Keep the current `Global TM` wording: rejected because it misrepresents runtime scope and becomes dangerous once Starfield TM is imported.
- Rename everything to `Skyrim TM`: rejected because Fallout and Starfield already exist in the same app surface and need the same abstraction.
- Add both `Franchise TM` and `Cross-Franchise TM` now: rejected because there is no validated data policy for safe cross-franchise promotion.

**Key risks**
- Broad rename churn across WPF bindings and generated MVVM command names.
- Accidental breakage of compare/import/export flows due to property renames.
- User confusion if on-disk path remains `global-glossary.sqlite` for Elder Scrolls but UI says `Skyrim/TES TM`.

**Mitigations**
- Keep on-disk path unchanged and document it as a legacy storage detail.
- Change public UI text first; only rename internal symbols where safe and mechanical.
- Add tests for path stability and TM hit behavior before and after rename.

---

### Task 1: Freeze the intended vocabulary and boundary

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Write the documentation change first**

```md
## Translation Memory Scope

- `Project TM`: entries learned or edited inside one project DB.
- `Franchise TM`: entries shared within the selected franchise only.
- Elder Scrolls keeps the legacy TM database path for backward compatibility, but the data is treated as `Skyrim/TES` scoped.
- Starfield and Fallout must use their own franchise TM stores.
```

- [ ] **Step 2: Add a short repo rule describing the intended boundary**

```md
## TM scope
- `Global TM` in older code/docs means franchise-scoped TM, not cross-franchise shared memory.
- Do not mix Starfield/Fallout/TES TM data unless an explicit promotion workflow exists.
```

- [ ] **Step 3: Run a docs-only sanity check**

Run: `rg -n "Global TM|Franchise TM|cross-franchise|Skyrim/TES TM" README.md AGENTS.md`
Expected: new terminology is present and no contradictory wording remains in those two files.

- [ ] **Step 4: Commit**

```bash
git add README.md AGENTS.md
git commit -m "docs: define franchise-scoped TM boundary"
```

### Task 2: Make storage semantics explicit without moving data

**Files:**
- Modify: `src/XTranslatorAi.App/Services/ProjectPaths.cs`
- Test: `tests/XTranslatorAi.Tests/FranchiseTranslationMemoryPathTests.cs`

- [ ] **Step 1: Write the failing path characterization tests**

```csharp
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;
using Xunit;

public class FranchiseTranslationMemoryPathTests
{
    [Fact]
    public void GetGlobalGlossaryDbPath_ElderScrolls_UsesLegacyPath()
    {
        var path = ProjectPaths.GetGlobalGlossaryDbPath(BethesdaFranchise.ElderScrolls);
        Assert.EndsWith("global-glossary.sqlite", path.Replace('\\', '/'));
        Assert.DoesNotContain("/starfield/", path.Replace('\\', '/'));
        Assert.DoesNotContain("/fallout/", path.Replace('\\', '/'));
    }

    [Fact]
    public void GetGlobalGlossaryDbPath_Starfield_UsesDedicatedDirectory()
    {
        var path = ProjectPaths.GetGlobalGlossaryDbPath(BethesdaFranchise.Starfield);
        Assert.Contains("/starfield/", path.Replace('\\', '/'));
    }
}
```

- [ ] **Step 2: Run the new test to verify current behavior is captured**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FranchiseTranslationMemoryPathTests`
Expected: pass if current behavior already matches, fail only if helper behavior differs from assumptions.

- [ ] **Step 3: Clarify the helper comments and, if useful, add an explicit alias helper**

```csharp
// Legacy name retained for on-disk compatibility.
// Conceptually this file stores franchise-scoped shared glossary/TM data.
public static string GetGlobalGlossaryDbPath(BethesdaFranchise franchise)
{
    ...
}

public static string GetFranchiseSharedDbPath(BethesdaFranchise franchise)
    => GetGlobalGlossaryDbPath(franchise);
```

- [ ] **Step 4: Re-run the targeted path tests**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter FranchiseTranslationMemoryPathTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/XTranslatorAi.App/Services/ProjectPaths.cs tests/XTranslatorAi.Tests/FranchiseTranslationMemoryPathTests.cs
git commit -m "refactor: characterize franchise TM storage paths"
```

### Task 3: Rename the app service boundary from global TM to franchise TM

**Files:**
- Modify: `src/XTranslatorAi.App/Services/GlobalTranslationMemoryService.cs`
- Modify: `src/XTranslatorAi.App/App.xaml.cs`
- Modify: `src/XTranslatorAi.App/Services/MainViewModelServices.cs`
- Modify: `src/XTranslatorAi.App/Services/TranslationRunnerService.cs`
- Modify: `src/XTranslatorAi.App/Services/CompareTranslationService.cs`

- [ ] **Step 1: Write or update a service-level test that proves the selected franchise controls TM loading**

```csharp
[Fact]
public async Task TranslationRunner_LoadsTranslationMemory_FromRequestedFranchiseDb()
{
    // Arrange separate franchise DBs and seed different TM values for the same source.
    // Act by building a run for Starfield and Elder Scrolls separately.
    // Assert each run receives the franchise-specific TM dictionary.
}
```

- [ ] **Step 2: Run the targeted TM tests to establish baseline**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter TranslationMemoryTests`
Expected: PASS before rename.

- [ ] **Step 3: Introduce the neutral service name and request field names**

```csharp
public sealed class FranchiseTranslationMemoryService
{
    private readonly GlobalProjectDbService _globalDbService;

    public FranchiseTranslationMemoryService(GlobalProjectDbService globalDbService)
    {
        _globalDbService = globalDbService;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDictionaryAsync(
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken)
    {
        var db = await _globalDbService.GetOrCreateAsync(cancellationToken);
        if (db == null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return await db.GetTranslationMemoryAsync(sourceLang, targetLang, cancellationToken);
    }
}
```

- [ ] **Step 4: Update DI and request records to use the new service and field names**

```csharp
var franchiseTranslationMemoryService = new FranchiseTranslationMemoryService(globalProjectDbService);
...
FranchiseTranslationMemory: franchiseTranslationMemory
```

- [ ] **Step 5: Re-run the focused TM test suite**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "TranslationMemoryTests|TranslationServiceTranslationMemoryValidationTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/XTranslatorAi.App/Services/GlobalTranslationMemoryService.cs src/XTranslatorAi.App/App.xaml.cs src/XTranslatorAi.App/Services/MainViewModelServices.cs src/XTranslatorAi.App/Services/TranslationRunnerService.cs src/XTranslatorAi.App/Services/CompareTranslationService.cs tests/XTranslatorAi.Tests
git commit -m "refactor: rename global TM service to franchise TM"
```

### Task 4: Rename the MainViewModel and tab surface to franchise-aware terminology

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalTranslationMemory.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/Tabs/IGlobalTranslationMemoryTabHost.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/Tabs/GlobalTranslationMemoryTabViewModel.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`

- [ ] **Step 1: Write a narrow UI-facing test or characterization check for command/property availability if a ViewModel test harness exists**

```csharp
[Fact]
public void MainViewModel_ExposesFranchiseTranslationMemoryCommands()
{
    // Instantiate MainViewModel with existing test doubles and assert the renamed commands/properties are non-null.
}
```

- [ ] **Step 2: Run the ViewModel-related tests that touch translation memory or franchise switching**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "Franchise|TranslationMemory|MainViewModel"`
Expected: existing related tests pass before rename.

- [ ] **Step 3: Rename the host-facing properties and methods mechanically**

```csharp
[ObservableProperty] private TranslationMemoryEntryViewModel? _selectedFranchiseTranslationMemoryEntry;
[ObservableProperty] private string _franchiseTranslationMemorySourceText = "";
[ObservableProperty] private string _franchiseTranslationMemoryDestText = "";
[ObservableProperty] private string _franchiseTranslationMemoryFilterText = "";
```

```csharp
private async Task ReloadFranchiseTranslationMemoryAsync()
{
    StatusMessage = $"{GetCurrentFranchiseTmLabel()} 불러오는 중...";
    ...
}
```

- [ ] **Step 4: Add a single label helper instead of scattering string conditionals**

```csharp
private string GetCurrentFranchiseTmLabel()
    => SelectedFranchise switch
    {
        BethesdaFranchise.ElderScrolls => "Skyrim/TES TM",
        BethesdaFranchise.Fallout => "Fallout TM",
        BethesdaFranchise.Starfield => "Starfield TM",
        _ => "Franchise TM",
    };
```

- [ ] **Step 5: Re-run the ViewModel-focused tests**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "Franchise|TranslationMemory|MainViewModel"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalTranslationMemory.cs src/XTranslatorAi.App/ViewModels/Tabs/IGlobalTranslationMemoryTabHost.cs src/XTranslatorAi.App/ViewModels/Tabs/GlobalTranslationMemoryTabViewModel.cs src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs tests/XTranslatorAi.Tests
git commit -m "refactor: make TM view-model franchise-aware"
```

### Task 5: Update WPF labels and messages to reflect the real scope

**Files:**
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`
- Modify: `src/XTranslatorAi.App/Views/GlobalTranslationMemoryTabView.xaml`
- Modify: `src/XTranslatorAi.App/Views/CompareTabView.xaml`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalTranslationMemory.cs`

- [ ] **Step 1: Replace hard-coded `Global TM` strings with franchise-aware wording**

```xml
<Button Content="TM 가져오기" ... ToolTip="선택한 프랜차이즈의 TM TSV를 가져옵니다." />
<TabItem Header="Franchise TM" ... />
<CheckBox Content="Franchise TM" ... ToolTip="선택한 프랜차이즈 TM을 적용합니다. TM hit가 있으면 LLM 호출 없이 완료될 수 있습니다." />
```

- [ ] **Step 2: Make runtime status strings use the label helper**

```csharp
StatusMessage = $"{GetCurrentFranchiseTmLabel()} 로드: {list.Count}개 항목";
StatusMessage = $"{GetCurrentFranchiseTmLabel()} 저장 완료: {applied}개 항목";
```

- [ ] **Step 3: Search for remaining contradictory UI text**

Run: `rg -n "Global TM|Global Translation Memory|global TM|global translation memory" src/XTranslatorAi.App`
Expected: only legacy internal names that are intentionally kept, or no results.

- [ ] **Step 4: Build the app to catch XAML/binding regressions**

Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add src/XTranslatorAi.App/MainWindow.xaml src/XTranslatorAi.App/Views/GlobalTranslationMemoryTabView.xaml src/XTranslatorAi.App/Views/CompareTabView.xaml src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalTranslationMemory.cs
git commit -m "feat: surface franchise-specific TM labels in UI"
```

### Task 6: Keep core translation semantics stable and update test intent

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslationMemory.cs`
- Modify: `tests/XTranslatorAi.Tests/TranslationMemoryTests.cs`
- Modify: `tests/XTranslatorAi.Tests/TranslationServiceTranslationMemoryValidationTests.cs`

- [ ] **Step 1: Rename test names and comments to match the new concept**

```csharp
[Fact]
public async Task TranslateIdsAsync_UsesFranchiseTranslationMemory_ForPendingRows()
{
    ...
}
```

- [ ] **Step 2: Update code comments without changing runtime precedence**

```csharp
// Project TM should override franchise TM when keys collide.
private static IReadOnlyDictionary<string, string> MergeTranslationMemory(...)
```

- [ ] **Step 3: Run the translation-memory test set**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "TranslationMemoryTests|TranslationServiceTranslationMemoryValidationTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/XTranslatorAi.Core/Translation/TranslationService.TranslationMemory.cs tests/XTranslatorAi.Tests/TranslationMemoryTests.cs tests/XTranslatorAi.Tests/TranslationServiceTranslationMemoryValidationTests.cs
git commit -m "test: align TM terminology with franchise scope"
```

### Task 7: Full verification and release-note readiness

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-04-08-franchise-tm-boundaries.md`

- [ ] **Step 1: Run the full test suite**

Run: `dotnet build tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release && dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build`
Expected: all tests pass.

- [ ] **Step 2: Run the app build**

Run: `dotnet build src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release`
Expected: build succeeds with no new errors.

- [ ] **Step 3: Write a short operator note for release/changelog**

```md
- `Global TM` is now documented and surfaced as franchise-scoped TM.
- Existing Elder Scrolls TM data is preserved.
- Starfield/Fallout TM remain isolated from Skyrim/TES TM.
```

- [ ] **Step 4: Mark completed tasks in this plan and commit the closeout updates**

```bash
git add README.md docs/superpowers/plans/2026-04-08-franchise-tm-boundaries.md
git commit -m "docs: finalize franchise TM refactor notes"
```

## Self-Review

- Spec coverage: the plan covers terminology, storage compatibility, service/view-model/UI rename, compare/runner wiring, and verification. It does not cover importing Starfield corpus data because that is explicitly out of scope.
- Placeholder scan: no `TBD`, `TODO`, or “similar to task” placeholders remain.
- Type consistency: the plan uses `Franchise TM` consistently for the feature concept while preserving the legacy DB path helper where needed.

## Rollback Notes

- If WPF binding churn causes too much risk, keep internal property names for one pass and limit the change to labels/comments plus a new `GetCurrentFranchiseTmLabel()` helper.
- Do not roll back the storage path characterization tests; they document current compatibility guarantees.
- If service renaming causes excessive generated-code churn, keep the type name but change comments/docs/UI first, then rename internals in a second pass.

Plan complete and saved to `docs/superpowers/plans/2026-04-08-franchise-tm-boundaries.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
