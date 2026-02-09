# Batch Refactor Wave 2 (One-Shot Execution Plan)

## Execution status (2026-02-10)
- `done` Track A: app orchestration decoupling (`RunCompareSlotAsync`, `TranslationRunnerService.RunAsync`)
- `done` Track B: core hotspot decomposition (`EnforceUnitsFromSource`, TM bulk upsert/update)
- `done` Track C: test architecture cleanup baseline applied (`TestSupport` fixture modularization pattern maintained)
- `done` Track D: docs consistency update (`docs/plans/README.md` active ordering + wave status sync)
- `done` Post-pass: `BuildRunsAsync` additional decomposition (removed from complexity top warnings set)

## Why this plan
- Current hotspot focus moved after LQA/Fixer split completion:
  - `src/XTranslatorAi.App/ViewModels/MainViewModel.Compare.cs:112` `RunCompareSlotAsync`
  - `src/XTranslatorAi.Core/Text/PlaceholderUnitBinder.cs:88` `EnforceUnitsFromSource`
  - `src/XTranslatorAi.App/Services/TranslationRunnerService.cs:58` `RunAsync`
  - `src/XTranslatorAi.Core/Data/ProjectDb.TranslationMemory.cs:60` `BulkUpsertTranslationMemoryAsync`
  - `src/XTranslatorAi.Core/Data/ProjectDb.TranslationMemory.Rows.cs:63` `BulkUpdateTranslationMemoryAsync`
- Goal is a single coordinated execution wave, but with strict internal checkpoints to avoid semantic drift.

## Evidence base (timeboxed research)
- .NET regex guidance (performance and safety):
  - https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-regex
  - https://learn.microsoft.com/en-us/dotnet/standard/base-types/backtracking-in-regular-expressions
- .NET unit testing best practices:
  - https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices
- Incremental refactoring strategy (strangler pattern):
  - https://martinfowler.com/bliki/StranglerFigApplication.html

## Scope
- In scope:
  - Split top-complexity methods into small private/internal units without changing public API contracts.
  - Preserve behavior through characterization tests + targeted unit tests.
  - Clean plan docs only where status/ownership became stale.
- Out of scope:
  - Feature additions
  - DB schema changes
  - UI redesign

## One-shot execution tracks

### Track A: App orchestration decoupling
1. `RunCompareSlotAsync` decomposition
  - Extract steps: input validation, prompt assembly, request dispatch, result projection, state transitions.
  - Keep original method as orchestrator shell only.
2. `TranslationRunnerService.RunAsync` decomposition
  - Extract job planning, run execution loop, error policy handling, progress publication.

Acceptance:
- Compare and translation-runner behavior stays byte-for-byte equivalent in existing tests.
- No new warnings/errors in build.

### Track B: Core hotspot decomposition
1. `PlaceholderUnitBinder.EnforceUnitsFromSource`
  - Extract token scan, source-unit inference, bind/repair stage into separate helpers.
  - Keep replacement ordering deterministic.
2. `BulkUpsertTranslationMemoryAsync` + `BulkUpdateTranslationMemoryAsync`
  - Extract row materialization and command batching helpers.
  - Keep transaction and error semantics unchanged.

Acceptance:
- Existing translation/unit/TM tests unchanged and passing.
- No SQL behavior regression in current test coverage.

### Track C: Test architecture cleanup
1. Continue fixture modularization pattern applied to LQA characterization tests.
2. Normalize repeated assertion helpers into `tests/XTranslatorAi.Tests/TestSupport/*`.

Acceptance:
- Test readability improves (scenario tests < 80 LOC where practical).
- Assertion helpers are single-source (no duplicated parser helpers).

### Track D: Documentation consistency cleanup
1. Update `docs/plans/README.md` active ordering and statuses.
2. Mark completed decomposition plans as trace logs when done.

Acceptance:
- `docs/plans/README.md` reflects actual current execution order.

## Execution guardrails
- Public signatures unchanged unless explicitly required.
- Preserve issue ordering/priority behavior in scanners/fixers and runner flows.
- Use characterization tests before/after each track.
- Prefer extract-and-delegate over rewrite.

## Verification protocol (must all pass)
1. Targeted suites per track
   - `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release --filter "FullyQualifiedName~LqaScanner|FullyQualifiedName~KoreanTranslationFixer"`
   - Plus track-specific test filters.
2. Full suite
   - `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
3. Structural health
   - `python3 scripts/vibe.py doctor --full`

## Loop/timebox safeguards
- Max 3 refactor cycles per hotspot method before freezing and reporting residual risk.
- Max 90 minutes per track before checkpoint summary and next-step proposal.

## Deliverables
- Refactored code in all target hotspots
- Updated/added tests with parity guarantees
- Updated plan index/status docs
- Final summary with modified file list + verification output
