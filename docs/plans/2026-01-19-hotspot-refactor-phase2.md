# Hotspot Refactor Phase 2 + Vibe-kit Accuracy Improvements

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve developer ergonomics and maintainability by (1) fixing vibe-kit complexity param counting for C# tuple/generic signatures and (2) refactoring the next top complexity hotspots without changing translation behavior.

**Architecture:** Keep changes small and local. Refactors should preserve existing semantics (especially placeholder/token handling and translation pipeline ordering), only extracting helpers and introducing small request/context records where it reduces parameter bloat and nesting.

**Tech Stack:** Python 3 (vibe-kit), .NET/C# (.NET 8), xUnit tests.

---

### Task 1: Fix vibe-kit complexity param counting (tuple/generics)

**Problem:** `.vibe/brain/check_complexity.py` uses a naive `split(",")` to count params, which over-counts commas inside tuple types and generics. This inflates `params=` and can mis-rank hotspots.

**Files:**
- Modify: `.vibe/brain/check_complexity.py`

**Step 1: Implement a depth-aware param splitter**
- Replace `_count_params` with a small parser that splits only on top-level commas, ignoring nested `< >`, `( )`, `[ ]`, `{ }`, and string literals.

**Step 2: Verify**
- Run: `python3 scripts/vibe.py doctor --full`
- Expected: complexity report still works, and tuple-heavy signatures no longer show inflated `params=`.

---

### Task 2: Refactor `TranslationCostEstimator.EstimateAsync` API to reduce param bloat

**Goal:** Reduce method parameter count below the vibe-kit warning threshold and simplify the method body without changing behavior.

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationCostEstimator.cs`
- Create: `src/XTranslatorAi.Core/Translation/TranslationCostEstimateRequest.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Tools.cs`

**Step 1: Add request record**
- Create a `public sealed record TranslationCostEstimateRequest` holding the former parameters (except CancellationToken).

**Step 2: Update estimator signature**
- Change to: `EstimateAsync(TranslationCostEstimateRequest request, CancellationToken cancellationToken)`
- Keep behavior identical (same clamping, same DB queries, same pricing loop).

**Step 3: Update call site**
- Update `MainViewModel.Tools.cs` to construct and pass the request record.

**Step 4: Verify**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `python3 scripts/vibe.py doctor --full`

---

### Task 3: Refactor `BuildProjectContextScanReportAsync` (reduce nesting/length)

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ProjectContext.cs`

**Step 1: Extract helpers**
- Extract private helpers for:
  - loading/merging glossary into dictionaries
  - scanning paged strings to accumulate counts/samples
  - building `TopRec`/`TopTerms` output

**Step 2: Preserve behavior**
- No changes to thresholds, filtering, or sample selection logic.

**Step 3: Verify**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `python3 scripts/vibe.py doctor --full`

---

### Task 4: Refactor `BuildWorkQueuesAsync` (reduce nesting/length)

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`

**Step 1: Extract helpers**
- Extract private helpers for:
  - partitioning items (short/long/veryLong)
  - sorting for batch consistency
  - building batches for each lane
  - configuring `_veryLongRequestGate` and `_maskedTokensPerCharHint`

**Step 2: Preserve semantics**
- Batch grouping/sorting must remain stable.
- Gate reservation logic must remain identical.

**Step 3: Verify**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `python3 scripts/vibe.py doctor --full`

