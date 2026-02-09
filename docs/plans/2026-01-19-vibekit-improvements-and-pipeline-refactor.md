# Vibe-kit Improvements + Pipeline Hotspot Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve vibe-kit ergonomics (bootstrap defaults, missing-deps messaging, CLI consistency) and reduce the next top complexity hotspots in the translation pipeline without changing runtime behavior.

**Architecture:** Keep public behavior stable. For vibe-kit, prefer small UX improvements and backwards-compatible aliases. For C# hotspots, reduce nesting/duplication by extracting private helpers and bundling the long parameter lists into small internal records (private to `TranslationService`).

**Tech Stack:** Python 3, .NET/C#, xUnit, repo-local vibe-kit (`scripts/vibe.py`).

---

### Task 1: Vibe-kit UX fixes (no functional changes to the app)

**Files:**
- Modify: `scripts/setup_vibe_env.py`
- Modify: `.vibe/brain/watcher.py`
- Modify: `scripts/vibe.py`
- Modify: `scripts/vibekit.py` (optional alias cleanup)
- Modify: `scripts/install_hooks.py`
- Modify: `AGENT_SYSTEM_PROMPT.md`

**Step 1: Update generated templates to prefer `python3` + `scripts/vibe.py`**
- Update `scripts/setup_vibe_env.py` defaults:
  - `DEFAULT_AGENT_CHECKLIST` should reference `python3 scripts/vibe.py ...`
  - `DEFAULT_PROFILE_GUIDE` should reference `python3 scripts/vibe.py ...`
- Keep existing on-disk `.vibe/*` files untouched unless necessary (avoid diff 폭탄).

**Step 2: Improve watcher missing-deps message**
- If `watchdog` import fails, print a one-line install hint:
  - `python3 scripts/vibe.py bootstrap --install-deps`
  - (fallback) `python3 -m pip install -r .vibe/brain/requirements.txt`
- Keep behavior otherwise unchanged.

**Step 3: Reduce CLI confusion**
- Add `precommit` subcommand to `scripts/vibe.py` (calls `.vibe/brain/precommit.py`).
- Optionally make `scripts/vibekit.py` delegate to `scripts/vibe.py` (so both entrypoints behave consistently).

**Step 4: Make git hook template prefer python3**
- Update `scripts/install_hooks.py` hook template to use `#!/usr/bin/env python3`.
- Keep Windows behavior unchanged (hook install is already no-op if `.git` is missing).

**Step 5: Verify vibe-kit**
- Run: `python3 scripts/vibe.py doctor --full`
- Expected: exits `0`, writes `.vibe/context/LATEST_CONTEXT.md`

---

### Task 2: Refactor `TranslateBatchWithRetriesAsync` (highest remaining complexity)

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.BatchRequests.cs`
- (Optional) Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SplitFallback.cs`

**Step 1: Introduce a small private context record**
- Create a `private sealed record BatchTranslateContext(...)` inside `TranslationService.Pipeline.BatchRequests.cs` to bundle:
  - api/model/systemPrompt/promptCache/langs/temperature/maxOutputTokens/responseSchema/maxRetries/enableRepairPass/cancellationToken
- Replace long parameter lists for internal helpers only (no public signature changes).

**Step 2: Extract helpers from `TranslateBatchWithRetriesAsync`**
- Extract:
  - `BuildBatchUserPrompt(...)` (collects prompt-only pairs, injects semantic hints)
  - `CreateBatchRequest(...)` (builds `GeminiGenerateContentRequest`)
  - `ParseAndValidateBatchMap(...)` (size/id checks)
  - `TryPopulateResultsAndRepairs(...)` (EnsureTokensPreservedOrRepair + semantic repair detection)
  - `ApplyRepairBatchesAsync(...)` (bounded repair batching + per-item fallback)
- Keep exception behavior identical (same thrown types, same retry logic).

**Step 3: Verify behavior by running tests**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: PASS

---

### Task 3: Reduce duplication/parameter bloat in split/single pipeline methods

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SplitFallback.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SingleRow.cs`

**Step 1: Introduce a shared pipeline context record**
- Create `private sealed record PipelineContext(...)` (api/model/systemPrompt/promptCache/langs/maxChars/temperature/maxOutputTokens/maxRetries/enableRepairPass/placeholderMasker/onRowUpdated/cancellationToken).
- Update `TranslateBatchWithSplitFallbackAsync` and `TranslateSingleRowAsync` to accept the record + a row/batch, keeping behavior the same.

**Step 2: Extract “finalize row” and “handle error” helpers**
- Move duplicated `try/catch` bodies into:
  - `FinalizeTranslationAsync(...)` (ApplyTokensAndUnmask + optional template fixer + learn session terms + DB update + duplicate propagation)
  - `HandleRowErrorAsync(...)` (Update status + notify + duplicate status update)
- Preserve cancellation behavior exactly (`OperationCanceledException` rethrow).

**Step 3: Verify**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `python3 scripts/vibe.py doctor --full`

