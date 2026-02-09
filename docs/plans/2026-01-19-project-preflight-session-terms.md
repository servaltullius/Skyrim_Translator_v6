# Project Preflight Session Terms Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Scan the project DB before translation to extract repeated English “term candidates”, let the user review/edit Korean targets, then preload them into session-term force tokens so concurrent batches stay consistent.

**Architecture:** Add a new “Preflight” tab in the WPF app. The tab scans `StringEntry.SourceText` to build a ranked list of TitleCase words/phrases (with counts + example snippets), optionally asks Gemini for draft Korean targets, then passes approved pairs into `TranslationService.TranslateIdsAsync` to seed `SessionTermMemory` before any requests.

**Tech Stack:** .NET (C#), WPF, CommunityToolkit.Mvvm, SQLite (ProjectDb), Gemini REST API client (`GeminiClient`).

---

### Task 1: Core API – preload session terms into TranslationService

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`

**Steps:**
1. Extend `TranslateIdsAsync` signature with an optional parameter:
   - `IReadOnlyList<(string Source, string Target)>? preloadedSessionTerms = null`
2. When `enableSessionTermMemory == true`, call `_sessionTermMemory.TryLearn(source, target)` for each preloaded pair before any batching/requests.
3. Keep behavior unchanged when parameter is null/empty.

**Verification:**
- `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 2: App – preflight scan view-model + term list

**Files:**
- Create: `src/XTranslatorAi.App/ViewModels/PreflightTermViewModel.cs`
- Create: `src/XTranslatorAi.App/ViewModels/MainViewModel.Preflight.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`

**Steps:**
1. Add `ObservableRangeCollection<PreflightTermViewModel> PreflightTerms` and basic filters (min count, max terms, text filter).
2. Implement `ScanPreflightTermsCommand`:
   - Iterate the DB with `GetStringCountAsync` + `GetStringsAsync(limit, offset)`
   - Extract TitleCase phrases/words (regex-based; normalize by stripping leading articles “The/A/An”)
   - Count occurrences; keep top N; attach 1–3 example snippets per term
   - Mark terms that already exist in project glossary as “known” and prefill target
3. Keep the scan conservative (avoid exploding lists; default min count ≥2, max terms ≤200).

---

### Task 3: App – optional Gemini “draft glossary” generator

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Preflight.cs`

**Steps:**
1. Add `SuggestPreflightTargetsCommand`:
   - Take currently listed terms that have empty target
   - Chunk the list (e.g., 30–60 terms per request)
   - Ask Gemini for JSON: `{ "terms": [ { "source": "...", "target": "..." } ] }`
   - Include small context: addon name, NexusContextPreview (if enabled), and a “keep consistent” instruction
2. Parse JSON safely; apply targets only when sources match.

---

### Task 4: Wire translation start to use preflight terms

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.cs`

**Steps:**
1. Collect `PreflightTerms.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.TargetTerm))`
2. Pass the pairs into `TranslateIdsAsync(..., preloadedSessionTerms: pairs, ...)`
3. Make it a no-op when the preflight list is empty.

---

### Task 5: UI – new “Preflight” tab

**Files:**
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**Steps:**
1. Add a new tab with:
   - Scan button
   - Min-count / max-terms controls
   - DataGrid for `PreflightTerms` (Enabled, Count, Source, Target, Examples)
   - Suggest (Gemini) button (optional) and “Apply on next translation” hint

---

### Task 6: Build / publish

**Commands:**
- Core tests: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Publish (Windows): `dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true`

