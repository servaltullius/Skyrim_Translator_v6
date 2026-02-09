# Translation Safety Overhaul Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce placeholder semantic errors (e.g., `<mag>초 동안`, `<dur>포인트`) without endless regex/template growth by relying on concise prompts, optional semantic hints, and strong validation/repair, while making template-based post-fixes optional and observable.

**Architecture:** Keep the existing “mask → translate → validate → unmask” pipeline. Add a small configuration surface to control (1) whether we do a semantic repair pass (soft vs strict), and (2) whether we apply the existing template-based post-fixer. Prefer deterministic validation and minimal prompts, aligning with Gemini 3 guidance, and keep rules/regex primarily for *protection + QA*, not for “translating by regex”.

**Tech Stack:** .NET 8, C# (Core + WPF app), xUnit for Core tests, Gemini API (AI Studio) via `GeminiClient`.

**Relevant docs (web research):**
- Gemini 3 dev guide: keep temperature at default 1.0; avoid low temperature for determinism (looping/degraded perf): https://ai.google.dev/gemini-api/docs/gemini-3
- Vertex AI Gemini 3 prompting guide: avoid overly broad negative constraints; keep constraints near the end: https://docs.cloud.google.com/vertex-ai/generative-ai/docs/start/gemini-3-prompting-guide
- Structured outputs: JSON schema gives syntactically-valid JSON but still needs semantic validation: https://ai.google.dev/gemini-api/docs/structured-output
- Localization best practice: protect placeholders and use QA checks: https://localazy.com/features/code-and-placeholders
- Post-translation QA for placeholders: https://poeditor.com/kb/how-to-preserve-variables-automatic-translation

---

### Task 1: Add “semantic repair strength” option (Soft/Strict)

**Files:**
- Create: `src/XTranslatorAi.Core/Translation/PlaceholderSemanticRepairMode.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TokenSanitization.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.BatchRequests.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SingleRow.cs`

**Design:**
- Add enum:
  - `Off`: never trigger “semantic repair” extra calls
  - `Soft`: trigger only on high-confidence semantic breaks (DUR not time / MAG|NUM used as time / missing semantic tokens)
  - `Strict`: current behavior (includes “bad particle” checks like `__XT_PH_MAG__와(과) 체력`)
- Keep existing `enableRepairPass` (it also controls repair for token-integrity failures). `semanticRepairMode` only controls the *additional* semantic repair triggers.

**Implementation notes:**
- Update `NeedsPlaceholderSemanticRepair(...)` to accept `PlaceholderSemanticRepairMode` and apply checks accordingly.
- Thread the mode through `TranslateIdsAsync(...)` and both batch/single-row pipelines.

**Verification:**
- Run Core tests: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 2: Add “template fixer” toggle (post-processing only)

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.BatchRequests.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SingleRow.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SplitFallback.cs`

**Design:**
- Add `enableTemplateFixer` flag (default `true` to preserve current behavior).
- When disabled, skip `MagDurPlaceholderFixer.Fix(...)` in the translation pipeline.
- Keep the manual tool action (“Fix placeholders” command in the UI) unchanged so users can still apply it explicitly.

**Verification:**
- Core tests: `dotnet test ...`

---

### Task 3: Wire new options into the app UI

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**Design:**
- Replace/augment the existing `EnableRepairPass` checkbox with:
  - `EnableRepairPass` (token-integrity repair + retry)
  - `SemanticRepairMode` dropdown (Soft/Strict) shown only when repair is enabled
- Add `EnableTemplateFixer` checkbox (default enabled).

**Verification (Windows):**
- `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true`

---

### Task 4: Add targeted regression tests for placeholder semantics

**Files:**
- Modify: `tests/XTranslatorAi.Tests/PlaceholderMaskerTests.cs`
- (Optional) Create: `tests/XTranslatorAi.Tests/PlaceholderSemanticHintInjectorTests.cs`

**Design:**
- Keep existing tests.
- Add at least:
  - `<100%>` inside angle brackets is labeled as numeric (already added)
  - Hint injection `Inject/Strip` round-trip removes markers safely

**Verification:**
- `dotnet test ...`

