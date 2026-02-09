# App Reanalysis + Rule Decomposition (2026-02-10)

> **For Claude/Codex:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` when implementing this plan task-by-task.

**Goal:** Re-analyze current app architecture and define enforceable decoupling rules that can be executed incrementally without behavior changes.

**Architecture:** Keep the current seam-first strategy, but tighten boundaries with explicit rules around composition root, ViewModel ownership, runner ports, and tab ownership. Apply rules in small refactor waves with parity tests and `vibe` health checks.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, SQLite(ProjectDb), vibe-kit diagnostics.

## 0) Execution Status (2026-02-10)

- `done` Wave A: translation runner host contract split into status/flow-control/failover ports.
- `done` Wave B: service composition moved to app startup composition root.
- `done` Wave C (partial target completed): compare translation pipeline extracted to `CompareTranslationService`; `MainViewModel.Compare` now coordinates UI slot state and delegates execution.
- `done` Wave D (Strings first slice): `StringsTabViewModel` now depends on `IStringsTabHost` port instead of concrete `MainViewModel`, reducing direct tab-to-shell coupling.
- `done` Wave D (Compare second slice): `CompareTabViewModel` now depends on `ICompareTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (ProjectGlossary third slice): `ProjectGlossaryTabViewModel` now depends on `IProjectGlossaryTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (GlobalGlossary fourth slice): `GlobalGlossaryTabViewModel` now depends on `IGlobalGlossaryTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (GlobalTranslationMemory fifth slice): `GlobalTranslationMemoryTabViewModel` now depends on `IGlobalTranslationMemoryTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (Prompt sixth slice): `PromptTabViewModel` now depends on `IPromptTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (ProjectContext seventh slice): `ProjectContextTabViewModel` now depends on `IProjectContextTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (ApiLogs eighth slice): `ApiLogsTabViewModel` now depends on `IApiLogsTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (Lqa ninth slice): `LqaTabViewModel` now depends on `ILqaTabHost` port instead of concrete `MainViewModel`.
- `done` Wave D (cleanup): removed now-unused `MainViewModelTabBase`.
- `done` Wave F (R5 first slice): introduced `IUiInteractionService` + `WpfUiInteractionService`; moved `MainViewModel` direct `MessageBox/OpenFileDialog/SaveFileDialog/Process.Start` calls behind the UI interaction boundary.
- `done` Wave F (R5 second slice): routed `App` startup fatal dialog and `SelectNexusModWindow` prompt through `IUiInteractionService`; direct WPF dialog/shell APIs are now centralized in `WpfUiInteractionService`.
- `done` Wave E: `architecture.rules` added to `.vibe/config.json`; `python3 scripts/vibe.py boundaries` now reports active rules and `ok: no violations` (not skipped).

## 1) Current Snapshot (Re-verified)

- Local time checked: `2026-02-10T05:42:04+09:00`.
- Build: `dotnet build XTranslatorAi.sln -c Release` -> success, warnings `0`, errors `0`.
- Tests: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --no-build` -> pass `211/211`.
- Structural diagnostics: `python3 scripts/vibe.py doctor --full` -> typecheck OK, no cycles, complexity hotspots still concentrated in Core pipeline and some App orchestration.

## 2) App-Layer Coupling Findings

1. `MainViewModel` is still the effective composition + orchestration center.
   - Service instantiation is performed inside VM constructor (`src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs:79`, `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs:91`).
2. App startup and VM both compose dependencies (double composition root smell).
   - Startup manual wiring exists in `src/XTranslatorAi.App/App.xaml.cs:27`, `src/XTranslatorAi.App/App.xaml.cs:40`.
3. `MainViewModel.Tools.cs` mixes multiple domains (model policy, UX dialogs, estimation, post-edits, TM import).
   - Representative command cluster from `src/XTranslatorAi.App/ViewModels/MainViewModel.Tools.cs:21` to `src/XTranslatorAi.App/ViewModels/MainViewModel.Tools.cs:507`.
4. Tab VMs are mostly pass-through adaptors, not independent feature owners.
   - `src/XTranslatorAi.App/ViewModels/Tabs/StringsTabViewModel.cs:14`
   - `src/XTranslatorAi.App/ViewModels/Tabs/CompareTabViewModel.cs:10`
5. Runner host contract still leaks UI-shaped types.
   - `ITranslationRunnerHost` includes `SavedApiKeyViewModel` and selection semantics (`src/XTranslatorAi.App/Services/ITranslationRunnerHost.cs:14`).

## 3) Rule Decomposition (Enforceable)

### R1. Single Composition Root
- Rule: only `App.xaml.cs` (or a dedicated bootstrapper) composes service object graphs.
- Violation evidence: `MainViewModel.Core.cs` direct `new ...Service(...)`.
- Done criteria: VM constructor receives already-built ports/services only.

### R2. VM Does Not Instantiate Domain Services
- Rule: ViewModel can orchestrate, but must not construct domain/service implementations.
- Violation evidence: `MainViewModel.Core.cs:91`-`98`.
- Done criteria: all service creation moved to bootstrap layer.

### R3. Runner Contract Uses Primitive Ports
- Rule: runner host contracts must expose behavior ports, not UI model types.
- Violation evidence: `SavedApiKeyViewModel` in `ITranslationRunnerHost`.
- Done criteria: `IApiKeyFailoverPort`, `IRunnerStatusPort`, `IRowUpdatePort`, `IPausePort` split and adopted.

### R4. Tab VM Owns Tab Use-Cases
- Rule: tab VM should expose tab-centric operations, not broad forwarding to `Main`.
- Violation evidence: repeated `get => Main.*`, `set => Main.*`.
- Done criteria: at least `Strings` and `Compare` tabs own commands/queries through dedicated facades.

### R5. UI Dialog Boundary
- Rule: `MessageBox/OpenFileDialog/SaveFileDialog/Process.Start` calls move behind UI service interfaces.
- Violation evidence: `MainViewModel.Tools.cs`, `MainViewModel.Project.cs`.
- Done criteria: dialog/shell operations mocked in tests.
- Progress: app-layer call sites (`MainViewModel`, `App`, `SelectNexusModWindow`) are now routed through `IUiInteractionService` (`WpfUiInteractionService` implementation). Direct WPF APIs remain only inside the concrete adapter.

### R6. State Segmentation
- Rule: split mutable state by domain (`TranslationState`, `GlossaryState`, `CompareState`) rather than one mega property surface.
- Violation evidence: `MainViewModel.Core.Properties.cs` has ~72 observable fields and many command invalidation links.
- Done criteria: command invalidation remains equivalent while property ownership is segmented.

### R7. Compare Flow Isolation
- Rule: compare translation pipeline must run through a dedicated compare service/facade, not inline DB lifecycle orchestration in VM.
- Violation evidence: `MainViewModel.Compare.cs:113`-`161`.
- Done criteria: compare slot runner is a service with VM as thin coordinator.

### R8. Architecture Guardrail in vibe-kit
- Rule: enforce boundaries in `.vibe/config.json` (or external boundaries rules file) so drift is caught by diagnostics.
- Violation evidence: `doctor --full` currently reports boundaries skipped (no rules).
- Done criteria: boundary checks produce actionable pass/fail output.

## 4) Execution Order (Small Safe Waves)

1. **Wave A: Contract split only (no behavior change)**
   - Split runner host interfaces and adapt existing `MainViewModel` implementation.
2. **Wave B: Composition cleanup**
   - Move service construction from `MainViewModel` to startup/bootstrap.
3. **Wave C: Compare + Tools seam extraction**
   - Extract compare runner and tool dialog/service adapters.
4. **Wave D: Tab ownership migration**
   - Convert `Strings` then `Compare` tab to feature-oriented facades.
5. **Wave E: Boundaries automation**
   - Add vibe boundary rules and wire into regular `doctor` checks.

## 5) Verification Protocol Per Wave

- `dotnet build XTranslatorAi.sln -c Release`
- `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- `python3 scripts/vibe.py doctor --full`
- Focused regression tests for touched area (runner/compare/dialog adapters).

## 6) External References (Research-first)

- MVVM separation guidance: https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm
- .NET DI guidelines: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines
- Incremental strangler migration pattern: https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig
