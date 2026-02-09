# Community Glossary + Fortify Prefix Hardening Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Align built-in glossary to community terms and reduce Fortify-list mistranslations by expanding shared-prefix lists before glossary/prompting (low false positives).

**Architecture:** Add a small, deterministic preprocessor that rewrites `Fortify X, Y and Z ...` into `Fortify X, Fortify Y and Fortify Z ...` after placeholder masking but before glossary tokenization. Update the embedded default glossary with community mappings and add a conservative migration for existing built-in DB rows (only for known old→new mappings).

**Tech Stack:** .NET 8, WPF, SQLite, xTranslator XML token contracts (`<mag>`, `<dur>`, `__XT_*__`).

---

### Task 1: Add Fortify shared-prefix list expander

**Files:**
- Create: `src/XTranslatorAi.Core/Text/FortifyListExpander.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.TranslateIds.Items.cs`
- Test: `tests/XTranslatorAi.Tests/FortifyListExpanderTests.cs`

**Steps:**
1. Write tests covering:
   - `Fortify Armor, Blocking and Smithing are ...` → prefix applies to each list item
   - No change when only 1 item or already expanded
2. Implement expander with strict parsing (only expand when list contains separators).
3. Wire expander between placeholder masking and glossary apply.
4. Run tests: `~/.dotnet/dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 2: Harden prompts for Fortify list semantics

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationPrompt.TextOnly.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationPrompt.cs`

**Steps:**
1. Add a concise rule: for patterns like `"Fortify X, Y and Z"`, interpret as `"Fortify X, Fortify Y, Fortify Z"`.
2. Keep token contract rules unchanged.
3. Run tests (same command as Task 1).

---

### Task 3: Update built-in glossary to community terms + safe migration

**Files:**
- Modify: `src/XTranslatorAi.App/Assets/기본용어집.md`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Glossary.cs`
- Modify: `src/XTranslatorAi.App/ViewModels/MainViewModel.Glossary.BuiltIn.cs`
- Test: `tests/XTranslatorAi.Tests/DefaultGlossaryMigrationTests.cs` (if practical)

**Steps:**
1. Update default glossary terms:
   - `Blocking` → `막기`
   - `Smithing` → `대장`
   - Add phrase entries:
     - `Fortify Armor` → `방어구 강화`
     - `Fortify Blocking` → `막기 강화`
     - `Fortify Smithing` → `대장 강화`
2. Add `Block` to prompt-only defaults to avoid forced replacements for a common/ambiguous word.
3. Add conservative built-in DB migration:
   - Only update known old targets (e.g., `Smithing: 제련 → 대장`, optionally `Block: 방어 → 막기`) for rows whose `Note` starts with `Built-in default glossary`.
4. Run tests.

---

### Task 4: Publish updated single-file EXE

**Files/Output:**
- Output: `artifacts/publish-win-x64-singlefile/TulliusTranslator.exe`

**Steps:**
1. Publish:
   - `~/.dotnet/dotnet publish src/XTranslatorAi.App/XTranslatorAi.App.csproj -c Release -r win-x64 -o artifacts/publish-win-x64-singlefile -p:PublishSingleFile=true -p:SelfContained=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:EnableWindowsTargeting=true`
2. User validates by launching the EXE.

