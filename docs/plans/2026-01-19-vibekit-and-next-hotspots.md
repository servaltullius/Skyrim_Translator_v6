# Vibe-kit UX Improvements + Next Hotspot Refactor Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make vibe-kit easier to use across sessions and more accurate, then refactor the next top complexity hotspots (`StartTranslationAsync`, `RunWorkersAsync`) without changing behavior.

**Architecture:** Keep changes small and local. For vibe-kit, focus on ergonomics and correctness (no new heavy deps). For refactors, extract private helpers and introduce small private context records to reduce parameter bloat and nesting while preserving cancellation/error semantics.

**Tech Stack:** Python 3, .NET/C#, xUnit, repo-local vibe-kit (`scripts/vibe.py`).

---

### Task 1: Fix indexer “changed≈” accuracy

**Files:**
- Modify: `.vibe/brain/indexer.py`

**Step 1: Make `index_file` return bool**
- Return `True` only when DB rows are updated (hash changed / new file).
- Return `False` when skipped (excluded, unreadable, same hash).

**Step 2: Use the bool for scan statistics**
- `scan_all` should count `changed` based on the returned bool (not timing heuristics).
- Keep output format stable (still prints `[indexer] done: ...`).

**Step 3: Verify**
- Run: `python3 scripts/vibe.py doctor --full`
- Expected: `[indexer] done: ... (changed≈0)` for no-op runs.

---

### Task 2: Make vibe-kit auto-recognizable in new sessions (AGENTS.md)

**Files:**
- Create: `AGENTS.md`

**Step 1: Add minimal repo instructions**
- Include:
  - Always read `.vibe/context/LATEST_CONTEXT.md` first
  - Prefer running `python3 scripts/vibe.py doctor --full`
  - Avoid diff 폭탄 / protect placeholder tokens
  - Windows equivalent command

**Step 2: Verify**
- Run: `python3 scripts/vibe.py doctor --full`
- Expected: no behavior change, just file presence.

---

### Task 3: Refactor `StartTranslationAsync` hotspot

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.cs`

**Step 1: Identify phases**
- Extract private helpers for:
  - preflight/validation
  - setting up translation request/options
  - progress callbacks and UI state changes
  - running translation + finalization

**Step 2: Preserve semantics**
- Same ordering, same cancellation and error handling.
- No behavior changes; only structure.

**Step 3: Verify**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 4: Refactor `RunWorkersAsync` hotspot

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`

**Step 1: Extract local functions into private helpers**
- Introduce a small private context record/class to pass shared state.
- Move `ProcessBatchAsync`/worker loop blocks into private methods.

**Step 2: Preserve scheduling behavior**
- Ensure same batch selection order, same throttle/gate rules, same error/status updates.

**Step 3: Verify**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 5: Final verification

**Step 1: Run doctor**
- Run: `python3 scripts/vibe.py doctor --full`

**Step 2: Run tests**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

