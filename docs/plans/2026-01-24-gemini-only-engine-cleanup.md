# Gemini-only Engine Cleanup Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Remove Qwen‑MT/DeepSeek from the app + core, keep Gemini as the only translation backend (default: `gemini-3-flash-preview`), and produce a Windows single-file build.

**Architecture:** `TranslationService` becomes Gemini-only. Engine selection and Qwen/DeepSeek-specific request fields are removed. UI keeps only Gemini API key profiles + Gemini model selection. Compare stays as a Gemini-model compare (3 slots).

**Tech Stack:** .NET 8, WPF (`XTranslatorAi.App`), `XTranslatorAi.Core`.

---

### Task 1: Remove Qwen‑MT/DeepSeek core client implementations

**Files:**
- Delete: `src/XTranslatorAi.Core/Translation/QwenMtClient.cs`
- Delete: `src/XTranslatorAi.Core/Translation/QwenMtClient.Types.cs`
- Delete: `src/XTranslatorAi.Core/Translation/DeepSeekClient.cs`
- Delete: `src/XTranslatorAi.Core/Translation/DeepSeekClient.Types.cs`
- Delete: `src/XTranslatorAi.Core/Translation/DeepSeekException.cs`

**Step 1: Verify compilation failures identify remaining references**

Run: `dotnet build XTranslatorAi.sln -c Release`
Expected: compile errors referencing `QwenMt*` / `DeepSeek*` types.

**Step 2: Remove remaining references**

Update remaining core files until build succeeds (next task handles engine/request types).

---

### Task 2: Simplify engine selection + request types to Gemini-only

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslateIdsRequest.cs`
- Delete or simplify: `src/XTranslatorAi.Core/Translation/TranslationEngine.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.*.cs` (remove Qwen/DeepSeek branches)

**Step 1: Make `TranslationService` constructor Gemini-only**

Ensure `TranslationService` only accepts `(ProjectDb db, GeminiClient gemini)`.

**Step 2: Remove Qwen/DeepSeek fields and code paths**

Remove Qwen/DeepSeek routing and any normalization branches, leaving a single Gemini pipeline.

**Step 3: Rebuild**

Run: `dotnet build XTranslatorAi.sln -c Release`
Expected: SUCCESS.

---

### Task 3: Remove Qwen‑MT/DeepSeek tests

**Files:**
- Delete: `tests/XTranslatorAi.Tests/QwenMtClientTests.cs`
- Delete: `tests/XTranslatorAi.Tests/TranslationServiceQwenMtSmokeTests.cs`
- Delete: `tests/XTranslatorAi.Tests/TranslationServiceDeepSeekSmokeTests.cs`

**Step 1: Run tests**

Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
Expected: PASS.

---

### Task 4: Publish Windows single-file build

**Files:**
- Output: `artifacts/publish-win-x64-single/`

**Step 1: Publish**

Run:
`dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-win-x64-single`

**Step 2: Verify output exists**

Expected: `artifacts/publish-win-x64-single/TulliusTranslator.exe`

