# TranslationService Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce the largest complexity hotspots reported by vibe-kit (especially `TranslateIdsAsync`) without changing runtime behavior.

**Architecture:** Keep the public API surface stable, but extract the `TranslateIdsAsync` workflow into smaller private helpers (and, where helpful, small private state records). Avoid behavior changes; focus on readability, testability, and easier future edits.

**Tech Stack:** C#/.NET, xUnit, repo-local vibe-kit (`scripts/vibe.py`).

---

### Task 1: Baseline + Safety Net

**Files:**
- Read: `.vibe/context/LATEST_CONTEXT.md`
- Run: `python3 scripts/vibe.py doctor --full`

**Step 1: Record current hotspots**
- Verify the current top hotspots include:
  - `src/XTranslatorAi.Core/Translation/TranslationService.cs` (`TranslateIdsAsync`)
  - `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.*.cs`
  - `src/XTranslatorAi.Core/Translation/TranslationCostEstimator.cs`

**Step 2: Run tests (baseline)**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: PASS

---

### Task 2: Extract `TranslateIdsAsync` phases into private helpers

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`
- Create (optional): `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`

**Step 1: Introduce minimal internal helper types**
- Add a small internal/private record for “run options” (api/model/lang/systemPrompt/batchSize/maxChars/maxConcurrency/temperature/maxOutputTokens/maxRetries/flags).
- Add a small internal/private record for callbacks (onRowUpdated/waitIfPaused/cancellationToken).

**Step 2: Extract phase helpers (no behavior change)**
- Move logic into helpers with clear names:
  - `TryCreatePromptCacheAsync(...)`
  - `LoadGlossaryAndInitSessionAutoGlossaryAsync(...)`
  - `BuildTranslationItemsAsync(...)` (includes translation-memory shortcut + duplicate detection)
  - `SeedSessionTermMemoryAsync(...)`
  - `BuildWorkQueuesAsync(...)` (short/long/very-long + sorting + token-per-char hint)
  - `RunWorkersAsync(...)` (worker allocation + queue draining)

**Step 3: Keep existing try/finally semantics**
- Ensure the same cleanup happens in the same cases:
  - gates disposed, caches deleted, session memory cleared, rowContext cleared

---

### Task 3: Reduce duplication in batch post-processing paths

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SingleRow.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SplitFallback.cs`

**Step 1: Extract a shared “finalize result” helper**
- Create/extend a helper method (likely in `TranslationService.Helpers.cs`) that:
  - `ApplyTokensAndUnmask(...)`
  - optional template fixer
  - session-term learning
  - duplicate row propagation
  - DB + callback updates

**Step 2: Update both single-row and batch paths to call the helper**
- Keep error handling and cancellation semantics identical.

---

### Task 4: Refactor `TranslationCostEstimator.EstimateAsync` hotspots

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationCostEstimator.cs`

**Step 1: Extract classification + batching helpers**
- Mirror the same extracted helpers used by `TranslationService` where possible (but keep estimator independent of the full pipeline).

**Step 2: Keep output identical**
- No changes to cost math, model list, or prompt templates.

---

### Task 5: Verify and update vibe-kit outputs

**Files:**
- Run: `python3 scripts/vibe.py doctor --full`
- Inspect: `.vibe/context/LATEST_CONTEXT.md`

**Step 1: Confirm reduced hotspot sizes**
- Expect `TranslateIdsAsync` to be significantly smaller and nesting reduced.

**Step 2: Run tests**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: PASS
