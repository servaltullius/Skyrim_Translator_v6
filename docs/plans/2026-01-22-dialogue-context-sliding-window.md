# Dialogue Context (Sliding Window) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Improve DIAL/INFO translation quality by injecting a small, safe “sliding window” context (prev/next lines) into prompts, without risking token/placeholder contract breakage.

**Architecture:** Precompute per-row dialogue context from the ordered ID list (xTranslator export order). For each DIAL/INFO row, group by EDID stem when available; otherwise fallback to local adjacency. Sanitize context lines to remove runtime tags/tokens (`<...>`, `[pagebreak]`, `__XT_*__`, `%…`) so the model cannot accidentally copy non-local tokens into the output. Inject context via a new optional `ctx` field on each prompt item (and append to `styleHint` for single-row text requests).

**Tech Stack:** .NET 8, existing `TranslationService` pipeline + prompt builders.

---

### Task 1: Add request flag + UI toggle

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslateIdsRequest.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.Run.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**Step 1: Add request flag**
- Add `EnableDialogueContextWindow` (default: true) to `TranslateIdsRequest` as an optional parameter at the end.
- Add `_enableDialogueContextWindow` to `TranslationService` run state and set it in `InitializeTranslateIdsRunState`.

**Step 2: Add UI checkbox**
- Add `EnableDialogueContextWindow` property to `MainViewModel`.
- Add a toolbar checkbox (e.g., “대화 문맥(윈도우)”) near `UseRecStyleHints`.
- Pass the property to `TranslateIdsRequest` in `BuildTranslateIdsRequest`.

**Verify:**
- Run: `dotnet build XTranslatorAi.sln -c Release`
- Expected: build succeeds.

---

### Task 2: Extend prompt item schema to carry context

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationPrompt.Types.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationPrompt.cs`

**Step 1: Add `ctx` field to `TranslationItem`**
- Add optional `ctx` property with `[JsonPropertyName("ctx")]`.

**Step 2: Update prompt rules**
- In `AppendJsonRules`, add a CRITICAL bullet:
  - `ctx` is reference-only; do not translate it; do not copy tokens from it; token preservation applies to the item’s `text` only.

**Verify:**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: pass.

---

### Task 3: Precompute dialogue context map (EDID-stem + adjacency fallback)

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.Items.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.TypesAndGates.cs`

**Step 1: Store ordered row metadata**
- While building translation items, record ordered `(id, rec, edid, sourceText)` for rows eligible for translation memory / translation.

**Step 2: Compute context per id**
- Only for `REC` base `DIAL`/`INFO`.
- Group selection:
  - If `NormalizeEdidStem(edid)` is non-empty: use same-stem neighbors within a bounded lookaround range.
  - Else: adjacency fallback: include only contiguous DIAL/INFO neighbors that also have no stable EDID stem.
- Window size: `prev=2`, `next=1` (fixed for MVP).
- Sanitize each context line:
  - Skip if it contains `<...>`, `[pagebreak]`, `__XT_`, `%`, `{0` etc.
  - Collapse whitespace and clamp length.
- Store a read-only `Dictionary<long, string>` on the service for the run.

**Step 3: Cleanup**
- Clear the context map in `CleanupTranslateIdsRunStateAsync`.

**Verify:**
- Run: `dotnet build XTranslatorAi.sln -c Release`

---

### Task 4: Inject context into batch + single-row prompts

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.BatchRequests.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SingleRow.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.LongText.cs`

**Step 1: Batch requests**
- In `BuildBatchUserPrompt`, set `TranslationItem.ctx` from the precomputed context map.

**Step 2: Single-row text requests**
- When `styleHint` exists for DIAL/INFO and a context exists, append it to `styleHint` as “Context (reference only)”.

**Verify:**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 5: Add a small unit test for prompt JSON including `ctx`

**Files:**
- Create: `tests/XTranslatorAi.Tests/TranslationPromptContextTests.cs`

**Step 1: Write failing test**
- Build a `TranslationItem` with `ctx`, call `TranslationPrompt.BuildUserPrompt`, and assert the serialized JSON contains `"ctx":`.

**Step 2: Run and confirm failure**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

**Step 3: Ensure implementation passes**
- (After Task 2) re-run tests.

