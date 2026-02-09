# Quality Escalation Model Routing (LQA-triggered)

> **Goal:** Use 3 Gemini models efficiently on the free tier by keeping most requests on a fast/cheap model, while selectively retrying “high-confidence bad outputs” on a higher-quality model.

## Decision (user-chosen)

- **Primary (most strings):** `gemini-2.5-flash-lite`
- **Book full texts:** `REC=BOOK:FULL` → `gemini-3-flash-preview` (already implemented as a separate run)
- **Quality escalation (selected items only):** retry on `gemini-2.5-flash` when a small, conservative set of LQA-style heuristics trigger.

Key constraint: keep **false positives low** and avoid large-scale “model mixing”.

## Architecture

### Where escalation happens

- Implement escalation **inside the core translation pipeline** (per-row), *before writing to DB*.
  - Reason: the core pipeline only translates `Pending`/`Error` items; post-hoc reruns won’t retranslate `Done` items without changing statuses.

### Runs

- App already performs a **two-phase translation** when `BOOK:FULL model override` is enabled:
  1) Run primary model for all non-`BOOK:FULL`
  2) Run book model for `BOOK:FULL`
- Quality escalation is applied **only in phase 1** to avoid downgrading book texts (e.g., `gemini-3-*` → `gemini-2.5-*`).

### Escalation triggers (conservative)

Escalate only when a row matches one of these “high-confidence” patterns:

- Unresolved particle markers remain: `을(를)`, `은/는`, etc.
- Obvious duplication artifacts: `효과 효과`, `초 초`, `<dur>초 초` …
- Percent formatting noise likely introduced by the model:
  - `%포인트` (e.g., `10%포인트`)
  - Hangul word followed by `%` (e.g., `밀어치기%`)

Non-goals (to keep FPs low):
- Don’t escalate on generic particle mismatch suggestions.
- Don’t escalate on glossary missing (solve via glossary/TM).
- Don’t escalate on “English residue” by default (proper nouns are common).

### Acceptance rule

If a trigger fires on the primary result:
- Retry once on the escalation model.
- Use the retried result only if it clears the triggering pattern.
- If retry fails (rate limit, timeout, etc.) or still triggers, keep the original result.

## UI/Settings

Add under “옵션”:
- Checkbox: **품질 재번역(Flash)**
- Model dropdown: escalation model (default `gemini-2.5-flash`)

Persist in `settings.json`:
- `enableQualityEscalation`
- `qualityEscalationModel`

## Testing

- Add unit tests for the new LQA heuristic helpers (pattern detection) to ensure low false positives.
- Existing test suite should continue to pass.

