# Strings TM Hit Highlighting Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** In the WPF app Strings tab, visually distinguish “TM-applied (exact match)” rows from “AI-translated” rows via a separate highlight color.

**Architecture:** When Translation Memory exact match is applied in `TranslationService`, persist a `StringNote` marker (`kind=tm_hit`). The app loads `tm_hit` notes into each `StringEntryViewModel.IsTranslationMemoryApplied`, and the DataGrid row style uses a dedicated brush when `Status==Done && IsTranslationMemoryApplied==true`.

**Tech Stack:** .NET, WPF, SQLite (`ProjectDb`), CommunityToolkit.Mvvm.

---

### Task 1: Add failing test for TM-hit marker

**Files:**
- Modify: `tests/XTranslatorAi.Tests/TranslationServiceTranslationMemoryValidationTests.cs`

**Step 1: Write the failing test**

Add a new `[Fact]` that:
- Seeds a single pending string with placeholders intact (e.g. `Absorb <mag> points...`)
- Provides `GlobalTranslationMemory` with a valid TM entry (includes `<mag>`)
- Runs `TranslateIdsAsync`
- Asserts:
  - LLM handler call count is `0` (TM applied)
  - `DestText` equals the TM output (after pipeline post-edits if any)
  - `GetStringNotesByKindAsync("tm_hit")` contains the id

**Step 2: Run test to verify it fails**

Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

Expected: FAIL because `tm_hit` is not recorded yet.

---

### Task 2: Record TM hit + add bulk delete API

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslationMemory.cs`
- Modify: `src/XTranslatorAi.Core/Data/ProjectDb.StringNote.cs`

**Step 1: Implement TM hit marker**
- Add `const string TmHitNoteKind = "tm_hit";`
- On successful TM apply, `UpsertStringNoteAsync(id, TmHitNoteKind, "TM 적용", ct)`

**Step 2: Add bulk delete**
- Add `DeleteStringNotesByKindAsync(string kind, CancellationToken ct)` to `ProjectDb`

**Step 3: Run tests**
- Run: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Expected: PASS

---

### Task 3: App loads/clears tm_hit and sets VM flag

**Files:**
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.StartHelpers.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Project.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Translation.Run.cs`

**Step 1: Clear stale tm_hit on translation start**
- In `ResetNonEditedTranslationsAsync()`:
  - Call `_db.DeleteStringNotesByKindAsync("tm_hit", ct)`
  - Set `IsTranslationMemoryApplied=false` for any non-Edited entries being reset

**Step 2: Load tm_hit on project load**
- At end of `LoadEntriesAsync()`, query `_db.GetStringNotesByKindAsync("tm_hit", ct)` and set `IsTranslationMemoryApplied=true` for matching ids.

**Step 3: Refresh tm_hit after translation ends**
- At end of `FinishTranslationUiStateAsync(...)`, refresh `tm_hit` flags so newly-TM-applied rows highlight correctly.

---

### Task 4: UI highlight color for TM-applied Done rows

**Files:**
- Modify: `src/XTranslatorAi.App/App.xaml`
- Modify: `src/XTranslatorAi.App/Views/StringsTabView.xaml`

**Step 1: Add brush**
- Add `XT.Row.DoneTmBrush` resource (distinct from `XT.Row.DoneBrush`)

**Step 2: Add row style trigger**
- Add a `MultiDataTrigger`:
  - `Status == Done` AND `IsTranslationMemoryApplied == True`
  - Set `Background` to `XT.Row.DoneTmBrush`
- Ensure it appears after the default Done trigger so it overrides it.

---

### Task 5: Build single-file app

**Step 1: Publish**
Run:
`~/.dotnet/dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableWindowsTargeting=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish-win-x64-single`

**Step 2: Verify output**
- Check `artifacts/publish-win-x64-single/` for the single-file exe.

