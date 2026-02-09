# Paired Slash List Expander Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Reduce Gemini translation errors where paired slash-separated lists (e.g., `A/B/C per X/Y/Z`) get misaligned or interleaved in Korean output.

**Architecture:** Add a small, deterministic preprocessor that rewrites the ambiguous `A/B/C per X/Y/Z` shape into an explicit, unambiguous mapping *without changing any `__XT_*__` token counts*. Run it after placeholder masking + glossary tokenization and before prompt construction so it works for both batch and single-item translation.

**Tech Stack:** .NET (C#), xUnit tests, existing `TranslationService` pipeline.

### Task 1: Add failing tests for the expander

**Files:**
- Create: `tests/XTranslatorAi.Tests/PairedSlashListExpanderTests.cs`

**Step 1: Write the failing test**
- Use reflection (`Type.GetType(...)`) so the test compiles before the expander exists.
- Input uses masked tokens (e.g., `__XT_PH_NUM_0000__`) to match real pipeline behavior.

**Step 2: Run test to verify it fails**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: FAIL because the expander type/method does not exist yet.

### Task 2: Implement the paired slash list expander (core)

**Files:**
- Create: `src/XTranslatorAi.Core/Text/PairedSlashListExpander.cs`

**Step 1: Minimal implementation**
- Implement `public static string Expand(string text)`:
  - Detect `__XT_PH_NUM_####__/...` + `per point/level of` + `X/Y/Z` patterns.
  - Rewrite to an explicit mapping form that preserves *exact token counts*.
  - No changes when pattern not detected or counts mismatch.

**Step 2: Run tests**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: PASS.

### Task 3: Wire the expander into translation pipeline

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`

**Step 1: Apply expander to masked+glossed text**
- After `PlaceholderMasker.Mask(...)` + `GlossaryApplier.Apply(...)`, call `PairedSlashListExpander.Expand(...)`.
- Store the expanded text as the per-row `Masked` text used for prompting (and repair prompts).

**Step 2: Add/adjust tests if needed**
- Ensure existing token-integrity tests remain green.

### Task 4: Verification + publish EXE

**Step 1: Run tests**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

**Step 2: Publish**
- Run:
  - `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true`

