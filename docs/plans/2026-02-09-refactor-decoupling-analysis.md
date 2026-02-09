# Refactor + Decoupling Analysis (2026-02-09)

## Scope
- Goal: start a practical refactor/decoupling design pass without changing user-visible behavior.
- Baseline: tests pass (`182/182`) on `tests/XTranslatorAi.Tests`.
- Constraint: keep existing public behavior stable; favor incremental seams over big-bang rewrites.

## Current Architecture Snapshot
- Solution shape is simple: `App (WPF)` -> `Core`, plus `Tests` and `Validate` tool.
- Startup has manual composition in `src/XTranslatorAi.App/App.xaml.cs`.
- `MainWindow.xaml` now binds tabs to per-tab VM instances (`StringsTab`, `CompareTab`, etc.).
- `MainViewModel` is split into many partial files, but remains the central state + orchestration owner.

## Key Findings (Code-Coupling)
1. `MainViewModel` still acts as a de facto god object.
   - Evidence: many service fields and responsibilities in `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`.
   - Tab VMs are mostly pass-through wrappers to `Main` (`src/XTranslatorAi.App/ViewModels/Tabs/StringsTabViewModel.cs`), so tab-level decoupling is still shallow.
2. `TranslationRunnerService` reduced size in VM, but coupling direction is still mixed.
   - It depends on `ITranslationRunnerHost` callbacks/properties that are implemented by `MainViewModel` (`src/XTranslatorAi.App/ViewModels/MainViewModel.TranslationRunnerHost.cs`), so the service still needs UI host semantics.
3. Core text pipeline hotspots are still monolithic.
   - `LqaScanner.ScanAsync` is long and rule-dense.
   - `KoreanTranslationFixer.Fix` is long and regex-driven with many branches.
4. Domain drift exists around Nexus.
   - Main tab-level UI is removed, but Nexus-related models/services still exist in Core and some App artifacts. This is acceptable if intentional, but documentation currently mixes "removed" and "still present" assumptions.

## Recommended Strategy (Incremental, Not Big-Bang)
### Option A (Recommended): Seam-first refactor
- Keep current UI behavior and progressively extract stable seams.
- Benefits: lower regression risk, easier verification, compatible with current test shape.
- Tradeoff: intermediate architecture is not "perfectly clean" for a while.

### Option B: Full shell/tab rewrite
- Rebuild `MainViewModel` into fully independent feature VMs in one cycle.
- Benefit: faster conceptual cleanup.
- Tradeoff: high regression risk in WPF binding/commands and existing workflows.

### Option C: Event-bus/mediator-first
- Introduce mediator/event bus and rewire features through messages.
- Benefit: strong decoupling.
- Tradeoff: large conceptual overhead vs current app scale.

## Target Refactor Design (for Option A)
1. Introduce `AppSessionState` as a narrow state aggregate.
   - Move cross-tab mutable state out of `MainViewModel` fields gradually.
2. Replace `ITranslationRunnerHost` breadth with focused ports.
   - Split into small contracts: status output, row updates, pause control, api-key failover policy.
   - Keep `TranslationRunnerService` UI-agnostic except for these ports.
3. Convert tab VMs from pass-through to feature adapters.
   - Each tab VM owns only its feature command/query surface, not raw forwarding of all `Main` members.
4. Split hotspot methods into rule units.
   - `LqaScanner`: rule pipeline (`ILqaRule`) with deterministic order.
   - `KoreanTranslationFixer`: grouped transform passes (particles, duration/percent, artifact cleanup).
5. Add architecture guardrails in vibe-kit config.
   - Explicit boundaries to prevent accidental app<->core leakage and to keep new services from growing into new god classes.

## Execution Roadmap
### Phase 0: Safety nets
- Freeze current behavior with targeted tests around translation orchestration and LQA/fixer edge cases.
- Keep existing integration points untouched.

### Phase 1: Runner decoupling
- Refactor `TranslationRunnerService` ports/contracts first.
- Exit criteria: no direct `MainViewModel` semantics in runner contract beyond explicit ports.

### Phase 2: Tab VM real ownership
- Migrate one tab at a time (`Strings` -> `LQA` -> `ProjectContext`), replacing pass-through properties with tab-owned operations.
- Exit criteria: each migrated tab can be unit-tested with mocked service ports.

### Phase 3: Core text pipeline modularization
- Break `LqaScanner` and `KoreanTranslationFixer` into small internal units with preserved ordering and tests.
- Exit criteria: same output for regression corpus; complexity hotspots reduced.

### Phase 4: Documentation + boundaries hardening
- Update plan index/status and mark superseded plans.
- Add/adjust boundary rules in `.vibe/config.json`.

## Documentation Hygiene (Decision)
- Keep old plan files for historical traceability.
- Introduce a canonical plan index (`docs/plans/README.md`) with status labels:
  - `active`
  - `historical`
  - `superseded`
- Mark Qwen/DeepSeek-specific plans as `superseded` by Gemini-only direction.

## External References Used
- .NET DI guidelines: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines
- .NET DI overview: https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/overview
- WPF data binding overview: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/
- C# async programming: https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/
- xUnit shared context: https://xunit.net/docs/shared-context
