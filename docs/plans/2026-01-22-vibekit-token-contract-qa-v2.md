# vibe-kit Token Contract QA v2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make `vibe qa` catch more “runtime contract” breakages in xTranslator XML (placeholders/tokens/format strings), output machine-readable results, and optionally fail the command for precommit/CI gating.

**Architecture:** Keep `qa_placeholders.py` streaming (`iterparse`) so huge XML files stay cheap. Extend it from “`<...>` placeholder multiset + basic `<mag>/<dur>` unit sanity” to a broader “token contract” extractor that validates multiple token families (tags, `[pagebreak]`, printf-like, .NET `{0}`), plus a hard error for leaked internal `__XT_*__` tokens. Keep the default UX compatible: `vibe qa file.xml` still writes a Markdown report and exits 0 unless `--fail-on` is requested.

**Tech Stack:** Python 3 (repo-local vibe-kit scripts), stdlib only (no new deps).

---

### Task 1: Extend token extraction + validation in `qa_placeholders.py`

**Files:**
- Modify: `.vibe/brain/qa_placeholders.py`

**Step 1: Add “token contract” extractors**
- Implement functions to extract (as exact text, not normalized):
  - Angle tags: `<...>` (existing)
  - Pagebreak markers: `[pagebreak]`
  - Internal tokens: `__XT_*__` (leak detector)
  - printf-like tokens: `%d`, `%0f`, `%02d`, `%s`, `%%` (best-effort)
  - .NET-like tokens: `{0}`, `{1:0.00}`, `{{`/`}}` handling (best-effort)

**Step 2: Compare multisets and emit typed findings**
- Keep the existing `placeholder_mismatch` finding, but add new kinds:
  - `pagebreak_mismatch`
  - `printf_token_mismatch`
  - `dotnet_format_mismatch`
  - `xt_token_leak` (any `__XT_*__` in `<Dest>`)
- Add a small severity mapping:
  - `error`: mismatches + `xt_token_leak`
  - `warn`: semantic unit checks (`mag_bad_unit`, `dur_bad_unit`)

**Step 3: Add output + exit control**
- Add CLI flags:
  - `--out <path>` (default remains `.vibe/reports/placeholder_qa.md`)
  - `--format md|json` (default `md`)
  - `--fail-on never|error|warn` (default `never`)
- For `--format json`, write a structured list (total scanned, counts by kind/severity, findings array).

**Step 4: Verify (smoke)**
- Run: `python3 scripts/vibe.py qa Druadach_english_korean1.xml --limit 20`
- Expected: a report file written under `.vibe/reports/…` and a 0 exit code.
- Run: `python3 scripts/vibe.py qa Druadach_english_korean1.xml --format json --out .vibe/reports/placeholder_qa.json --fail-on error`
- Expected: JSON file written; exit is 0 when no “error” findings, otherwise non-zero.

---

### Task 2: Expose new QA flags at the top-level `vibe` command

**Files:**
- Modify: `scripts/vibe.py`

**Step 1: Add new flags to `vibe qa` subparser**
- Mirror `qa_placeholders.py` flags: `--out`, `--format`, `--fail-on`.

**Step 2: Forward unknown args for extensibility**
- Keep parsing stable, but forward `rest` args to the underlying brain script so future flags don’t require wrapper edits.

**Step 3: Verify**
- Run: `python3 scripts/vibe.py qa --help`
- Expected: new flags visible and the command still works with old usage.

---

### Task 3: Update vibe-kit docs with the new QA behavior

**Files:**
- Modify: `.vibe/README.md`
- Modify: `.vibe/AGENT_CHECKLIST.md`

**Step 1: Add examples**
- Add a short “QA v2” example block showing:
  - Markdown report usage (default)
  - JSON output usage
  - `--fail-on error` gating usage

**Step 2: Verify**
- Run: `python3 scripts/vibe.py --help`
- Expected: docs are consistent with CLI.

---

### Task 4: Optional quality gate wiring (follow-up)

**Files:**
- Modify: `.vibe/brain/precommit.py`
- Modify: `.vibe/config.json`

**Idea:** Add an optional config flag + CLI toggle so `vibe precommit` can run `vibe qa <some.xml>` when a repo has a “current QA target XML”.

**Note:** Keep this out of the first implementation unless you actively want precommit to be XML-aware.

