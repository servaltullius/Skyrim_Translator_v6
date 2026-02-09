# App LQA (Review) Tab Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add an in-app “LQA (Review)” tab that scans completed translations for common issues and lets the user jump to the string and fix it directly.

**Architecture:** Implement LQA scanning in `MainViewModel` (no DB schema changes). The scan produces an in-memory list of issues (severity + code + message + row id) displayed in a new WPF tab. Selecting an issue focuses the existing `SelectedEntry` so the user can edit and save the translation using the existing “Save Dest” flow.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, existing `StringEntryViewModel` + `MainViewModel` patterns.

---

### Task 1: Add LQA issue list state + filtering

**Files:**
- Create: `src/XTranslatorAi.App/ViewModels/LqaIssueViewModel.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Core.Properties.cs`

**Steps:**
1. Add an `ObservableRangeCollection<LqaIssueViewModel>` and an `ICollectionView` with a text filter (like Preflight).
2. Add basic UI state:
   - `IsLqaScanning` (to prevent concurrent scan)
   - `LqaFilterText` (search box)
   - `SelectedLqaIssue` (DataGrid selection)
3. When `SelectedLqaIssue` changes, set `SelectedEntry` to the matching `StringEntryViewModel` (jump-to-row UX).

**Verify (compile):**
- Run: `dotnet build XTranslatorAi.sln -c Release`
- Expected: build succeeds.

---

### Task 2: Implement LQA scanning rules (MVP)

**Files:**
- Create: `src/XTranslatorAi.App/ViewModels/MainViewModel.Lqa.cs`
- Create: `src/XTranslatorAi.App/ViewModels/MainViewModel.Lqa.Scan.cs`

**Rules (initial MVP):**
- Token/Tag contract mismatch (source vs dest): reuse existing UI token multiset logic.
- English residue (for Korean target): detect `[A-Za-z]{2,}` in output (warn).
- “Unresolved particle” marker leftover: `을(를)`, `은(는)`, `을/를`, `은/는` (warn).
- Bracket/parenthesis mismatch: `()`, `[]` count mismatch (warn).
- Length risk (warn):
  - Only for `REC` base `QUST`/`MESG`.
  - Flag when the translated text is unusually long (absolute threshold + relative ratio vs source).
- REC-based baseline tone (warn/info):
  - `BOOK`: prefer 서술체(…다/…한다) vs “요/습니다”.
  - `QUST`/`MESG`: prefer 합니다체 vs “요/…다”.
- Tone inconsistency *within dialogue groups* (optional, warn):
  - Only for `REC` base `DIAL`/`INFO`.
  - Group by EDID-stem first; if no stable EDID stem, fallback to adjacency blocks.
  - Only flag when there is a strong majority tone (avoid false positives).

**UX:**
1. Add `Scan` and `Clear` commands, disabled while translating.
2. Update `StatusMessage` during scanning to show progress.
3. Store issues sorted by severity then `OrderIndex`.

**Verify:**
- Open any project and run scan; selecting an issue focuses the correct row.

---

### Task 3: Add WPF “LQA (Review)” tab UI

**Files:**
- Create: `src/XTranslatorAi.App/Views/LqaTabView.xaml`
- Create: `src/XTranslatorAi.App/Views/LqaTabView.xaml.cs`
- Modify: `src/XTranslatorAi.App/MainWindow.xaml`

**UI layout (MVP):**
1. Top toolbar: `Scan`, `Clear`, search box.
2. DataGrid of issues (severity, code, #, EDID, REC, message, source/dest preview).
3. Detail pane: reuse the same “Source/Dest” editing pattern as Strings tab, bound to `SelectedEntry`.

**Verify (compile):**
- Run: `dotnet build XTranslatorAi.sln -c Release`
- Expected: build succeeds and the new tab loads at runtime.

---

### Task 4: Verification

**Commands:**
- Run: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Run: `dotnet build XTranslatorAi.sln -c Release`

**Expected:**
- Tests pass.
- Solution builds.
