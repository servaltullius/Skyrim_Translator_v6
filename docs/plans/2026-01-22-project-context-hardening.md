# Project Context Hardening + Core Refactor Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Reduce “Project context generation failed: Gemini response is missing 'context'.” by hardening response parsing and (next) moving scan/report generation out of `MainViewModel` into Core for testability.

**Architecture:** Introduce a Core parser that extracts `"context"` from imperfect model output (code fences, extra text, nested JSON). Add a single retry path (stricter instruction) only when parsing fails. Then extract the scan/report builder (REC counts, term extraction, samples) into Core and keep `MainViewModel` as orchestration only.

**Tech Stack:** .NET 8, WPF, `System.Text.Json`, existing `ProjectDb`, existing `GeminiClient`.

---

### Task 1: Add tolerant Project Context response parser (Core)

**Files:**
- Create: `src/XTranslatorAi.Core/Text/ProjectContext/ProjectContextResponseParser.cs`
- Test: `tests/XTranslatorAi.Tests/ProjectContextResponseParserTests.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ProjectContext.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ProjectContext.Prompt.cs` (if needed)

**Behavior (low-risk defaults):**
- Accept valid JSON object with `"context"`.
- If parsing fails, try extracting the first JSON object/array substring from the output and parse again.
- If JSON parses, search for `"context"` case-insensitively at any depth.
- Only if still missing, treat the output as invalid (no “raw text becomes context” fallback by default).

**Verify:**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: new tests pass.

---

### Task 2: Add one retry on parse failure (App orchestration)

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ProjectContext.cs`

**Behavior:**
- If the first Gemini call returns output that cannot be parsed into `"context"`, perform exactly one retry with an even stricter system instruction (“ONLY JSON object with key context”).
- Surface a short user-facing status message like “Retrying (invalid JSON output)…”.
- Keep detailed diagnostics in API logs only.

**Verify (manual):**
- Attempt `Generate Project Context` with the same model that previously failed; it should either succeed or fail with a clearer message after a single retry.

---

### Task 3: Extract Project Context scan/report generation into Core

**Files:**
- Create: `src/XTranslatorAi.Core/Text/ProjectContext/ProjectContextScanner.cs`
- Create: `src/XTranslatorAi.Core/Text/ProjectContext/ProjectContextScanReport.cs`
- (Optional) Create: `src/XTranslatorAi.Core/Text/ProjectContext/ProjectContextTermExtraction.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ProjectContext.Scan.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.ProjectContext.Prompt.cs` (type import)
- (Cleanup) Delete or trim: `src/XTranslatorAi.App/ViewModels/MainViewModel.TermExtraction.cs`

**Goal:**
- Keep JSON report shape stable (same property names/structure).
- Keep sampling heuristics and limits stable.

**Tests:**
- Add a small DB-backed test that seeds a few strings and asserts:
  - Top REC counts and sample selection is deterministic.
  - Term extraction returns expected keys and counts.

**Verify:**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `python3 scripts/vibe.py doctor --full`

---

### Task 4: Publish single-file build

**Verify:**
- Run: `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true`

**Output:**
- Copy the resulting EXE to: `C:\\Users\\kdw73\\Downloads\\TulliusTranslator.exe`
- Provide SHA256 for verification.

