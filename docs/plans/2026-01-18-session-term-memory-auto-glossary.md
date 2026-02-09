# Session Term Memory Auto-Glossary Implementation Plan

> **For Codex:** Implement in this workspace (no subagents/worktrees).

**Goal:** Make name/title strings translate consistently by forcing learned session terms as `__XT_TERM_SESS_####__` tokens, and persist newly learned terms into the project Glossary so users can edit them.

**Architecture:** Extend `TranslationService`’s session term memory to (1) assign stable per-term `__XT_TERM_SESS_####__` tokens, (2) replace matching source phrases in masked text token-safely before requesting Gemini, and (3) insert learned mappings into SQLite `Glossary` with an “Auto(Session)” category so the WPF Glossary grid can edit/disable them.

**Tech Stack:** .NET 8, C#, xUnit, SQLite (`Microsoft.Data.Sqlite`).

---

### Task 1: Define forcing + persistence rules

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.SessionTermMemory.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.cs`

**Rules:**
- Learn only from `REC` strings matching `*:FULL/*:NAME/*:NAM/*:TITLE` (existing behavior).
- Force-match only in plain text (never inside `__XT_*__` tokens).
- Use `__XT_TERM_SESS_####__` tokens so they never collide with glossary tokens (`__XT_TERM_####__`).
- Persist newly learned terms into DB Glossary as:
  - `Category`: `Auto(Session)`
  - `ForceMode`: `ForceToken`
  - `MatchMode`: `Substring`
  - `Priority`: 20
  - `Note`: indicates auto-learn origin
- Never overwrite an existing glossary entry for the same normalized source term.

---

### Task 2: Add session-term force tokenization (TDD)

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.SessionTermMemory.cs`
- Test: `tests/XTranslatorAi.Tests/SessionTermMemoryForceTokenTests.cs`

**Step 1: Write failing test**
- Given a learned term `Ancient Dragons' Lightning Spear => 고룡의 뇌창`, when input text contains that phrase, it should be replaced with `__XT_TERM_SESS_0000__` only in plain text, not inside `__XT_PH_####__`.

**Step 2: Implement minimal code**
- Store per-term token in session memory.
- Add a token-safe substring replace routine.

**Step 3: Run tests**
- `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`

---

### Task 3: Persist learned terms into project Glossary (TDD)

**Files:**
- Modify: `src/XTranslatorAi.Core/Data/ProjectDb.Glossary.cs`
- Modify: `src/XTranslatorAi.Core/Translation/TranslationService.SessionTermMemory.cs`
- Test: `tests/XTranslatorAi.Tests/ProjectDbGlossarySessionAutoInsertTests.cs`

**Steps:**
- Add a DB helper to check/insert without duplicates (case-insensitive, normalized).
- Call it only when a session term is newly learned.

---

### Task 4: Update prompts to mention `__XT_TERM_SESS_####__`

**Files:**
- Modify: `src/XTranslatorAi.Core/Translation/TranslationPrompt.cs`

**Steps:**
- Extend token examples/rules to explicitly include `__XT_TERM_SESS_0000__`.

---

### Task 5: Verify

Run:
- `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
